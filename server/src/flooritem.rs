//! Items on the ground: map.cpp map_addflooritem / map_clearflooritem / map_clearflooritem_timer /
//! map_cell_free / map_search_freecell_dist, pc.cpp pc_dropitem / pc_takeitem /
//! pc_candrop, party.cpp party_share_loot (party.rs share_loot), clif.cpp clif_dropflooritem / clif_getareachar_item /
//! clif_takeitem and clif_parse_DropItem / clif_parse_TakeItem.
//!
//! A floor item is a `unit` row of kind BL_ITEM (its block_list: id, map, x, y) plus a `flooritem` row with the rest
//! of flooritem_data (the item, loot priority, subx / suby, the clear timer).

use spacetimedb::{table, Table};

use crate::clif::{self, Target};
use crate::db::items as itemdb;
use crate::inventory::*;
use crate::map::{self, CellChk, BL_ITEM};
use crate::net::packets::*;
use crate::pc;
use crate::timer::{self, Func};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};
use crate::unit::unit as _;

/// conf/battle/drops.conf `flooritem_lifetime: 60000`, `item_first_get_time: 3000`, `item_second_get_time: 2000`,
/// `item_third_get_time: 2000`, `mvp_item_first_get_time: 10000`, `mvp_item_second_get_time: 10000`,
/// `mvp_item_third_get_time: 2000`.
pub fn flooritem_lifetime() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::FLOORITEM_LIFETIME) as i64
}
pub fn item_first_get_time() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::ITEM_FIRST_GET_TIME) as i64
}
pub fn item_second_get_time() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::ITEM_SECOND_GET_TIME) as i64
}
pub fn item_third_get_time() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::ITEM_THIRD_GET_TIME) as i64
}
pub fn mvp_item_first_get_time() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::MVP_ITEM_FIRST_GET_TIME) as i64
}
pub fn mvp_item_second_get_time() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::MVP_ITEM_SECOND_GET_TIME) as i64
}
pub fn mvp_item_third_get_time() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::MVP_ITEM_THIRD_GET_TIME) as i64
}
/// conf/battle/misc.conf `item_stacking: yes`; conf/battle/items.conf `item_onfloor` (battle.cpp default 1:
/// untradeable items vanish when dropped — pc_dropitem bypasses it with flag 4).
pub fn item_stacking() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::ITEM_STACKING) != 0
}
pub fn item_onfloor() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::ITEM_FLOORITEM_CHECK) != 0
}
/// conf/battle/drops.conf `rndopt_drop_pillar` (battle.cpp default 1).
pub fn rndopt_drop_pillar() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::RNDOPT_DROP_PILLAR) != 0
}

/// flooritem_data (map.hpp) without its block_list, which is the unit row.
#[table(accessor = flooritem)]
#[derive(Clone, Debug)]
pub struct FloorItem {
    #[primary_key]
    pub id: u32,
    pub subx: u8,
    pub suby: u8,
    pub cleartimer: u64,
    pub first_get_charid: u32,
    pub second_get_charid: u32,
    pub third_get_charid: u32,
    pub first_get_tick: Tick,
    pub second_get_tick: Tick,
    pub third_get_tick: Tick,
    pub item: Item,
    /// ID of the monster that dropped it (0: not a monster drop).
    pub mob_id: u16,
}

/// map.cpp map_cell_free: passable, not within 5 (SW) / 4 (NE) cells of the edge, no units of `kinds` on it.
pub fn map_cell_free(c: &Ctx, m: u16, x: i16, y: i16, kinds: u16) -> bool {
    let Some(md) = crate::data::maps().get(m) else { return false };
    let edge_valid = map::map_edge_size().min(5);
    if x < edge_valid || x > md.xs - edge_valid || y < edge_valid || y > md.ys - edge_valid {
        return false;
    }
    if map::getcell(c, m, x, y, CellChk::ChkNoPass) {
        return false;
    }
    if kinds != 0 && map::count_oncell(c, m, x, y, kinds, 0) > 0 {
        return false;
    }
    true
}

/// map.cpp map_search_freecell_dist: a random free cell around (x, y) at distance distmin..distmax, checking each
/// quadrant and line once (the quadrant order mirrored at random).
pub fn map_search_freecell_dist(c: &Ctx, m: u16, x: i16, y: i16, distmin: i16, distmax: i16, kinds: u16) -> Option<(i16, i16)> {
    let mirrorx: i16 = if c.rnd() % 2 != 0 { -1 } else { 1 };
    let mirrory: i16 = if c.rnd() % 2 != 0 { -1 } else { 1 };
    for i in -1i16..=1 {
        for j in -1i16..=1 {
            if i != 0 || j != 0 {
                let cx = x + mirrorx * i * c.rnd_value(distmin as i32, distmax as i32) as i16;
                let cy = y + mirrory * j * c.rnd_value(distmin as i32, distmax as i32) as i16;
                if map_cell_free(c, m, cx, cy, kinds) {
                    return Some((cx, cy));
                }
            }
        }
    }
    None
}

/// map.cpp map_addflooritem. flags: &1 MVP item, &2 search a 5x5 area instead of 3x3, &4 bypass the drop check.
/// `dir` None = DIR_MAX (a random cell around the target), Some(-1) = DIR_CENTER. Returns the floor item id (0: fail).
#[allow(clippy::too_many_arguments)]
pub fn map_addflooritem(c: &Ctx, item: &Item, amount: i32, m: u16, x: i16, y: i16, first_charid: u32, second_charid: u32, third_charid: u32, flags: i32, mob_id: u16, can_show_effect: bool, dir: Option<i8>, kinds: u16) -> u32 {
    if flags & 4 == 0 && item_onfloor() && itemdb::itemdb_traderight(item.nameid).trade {
        return 0; // can't be dropped
    }
    let (mut x, mut y) = (x, y);
    const DIR_CENTER: i8 = -1;
    if let Some(d) = dir {
        if d > DIR_CENTER && (d as u8) < crate::path::DIR_MAX {
            x += crate::path::DIRX[d as usize];
            y += crate::path::DIRY[d as usize];
        }
    }
    // If cell occupied and not center cell, drop item around the drop target cell
    if dir.is_none() || (dir != Some(DIR_CENTER) && !map_cell_free(c, m, x, y, kinds)) {
        match map_search_freecell_dist(c, m, x, y, 1, if flags & 2 != 0 { 2 } else { 1 }, kinds) {
            Some((nx, ny)) => {
                x = nx;
                y = ny;
            }
            None => {
                // Only stop here if BL_ITEM shall not stack, otherwise drop on original target cell
                if kinds & BL_ITEM != 0 {
                    return 0;
                }
            }
        }
    }
    let id = crate::map::get_new_object_id(c);
    if id == 0 {
        return 0;
    }
    // If item is flagged as MVP item or dropped by bosses, it is protected for longer
    let mut extend_protection = flags & 1 != 0;
    if !extend_protection && mob_id > 0 {
        if let Some(mob) = crate::db::mobs::mob_db().find(mob_id as u32) {
            if mob.get_bosstype() != crate::db::mobs::BossType::None {
                extend_protection = true;
            }
        }
    }
    let tick = c.tick();
    let (t1, t2, t3) = if extend_protection {
        let a = tick + mvp_item_first_get_time();
        let b = a + mvp_item_second_get_time();
        (a, b, b + mvp_item_third_get_time())
    } else {
        let a = tick + item_first_get_time();
        let b = a + item_second_get_time();
        (a, b, b + item_third_get_time())
    };
    let mut it = item.clone();
    it.amount = amount as i16;
    let subx = (c.rnd_value(1, 4) * 3) as u8;
    let suby = (c.rnd_value(1, 4) * 3) as u8;
    let cleartimer = timer::add(c, tick + flooritem_lifetime(), Func::MapClearFloorItem, id, 0);
    c.db().flooritem().insert(FloorItem {
        id,
        subx,
        suby,
        cleartimer,
        first_get_charid: first_charid,
        second_get_charid: second_charid,
        third_get_charid: third_charid,
        first_get_tick: t1,
        second_get_tick: t2,
        third_get_tick: t3,
        item: it,
        mob_id,
    });
    // map_addiddb + map_addblock
    let mut u = Unit {
        id,
        kind: BL_ITEM,
        map: m,
        x,
        y,
        on_map: false,
        name: String::new(),
        vd: Default::default(),
        ud: Default::default(),
        sc: Default::default(),
        speed: 0,
        mode: 0,
        level: 0,
        char_id: 0,
        head_dir: 0,
        font: 0,
        manner: 0,
        karma: false,
        guild_id: 0,
        emblem_id: 0,
        hp: 0,
        max_hp: 0,
        dead: false,
        size: 0,
        boss: 0,
        invisible: false,
        npc_owner_char: 0,
    };
    c.db().unit().insert(u.clone());
    if !unit::addblock(c, &mut u) {
        return 0;
    }
    clif_dropflooritem(c, id, can_show_effect);
    id
}

/// map.cpp map_clearflooritem: the item leaves the map (picked up / removed).
pub fn map_clearflooritem(c: &Ctx, id: u32) {
    let Some(f) = c.db().flooritem().id().find(id) else { return };
    if f.cleartimer != 0 {
        timer::delete(c, f.cleartimer);
    }
    if let Some(u) = unit::get(c, id) {
        clif::clearflooritem(c, &u, None);
    }
    c.db().flooritem().id().delete(id);
    c.db().unit().id().delete(id);
    unit::bump_mob_pos_gen();
}

/// map.cpp map_clearflooritem_timer: the item's lifetime ran out.
pub fn map_clearflooritem_timer(c: &Ctx, tid: u64, id: u32) {
    let Some(f) = c.db().flooritem().id().find(id) else {
        log::error!("map_clearflooritem_timer : error");
        return;
    };
    let Some(u) = unit::get(c, id).filter(|u| u.kind == BL_ITEM) else {
        log::error!("map_clearflooritem_timer : error");
        return;
    };
    if f.cleartimer != tid {
        log::error!("map_clearflooritem_timer : error");
        return;
    };
    if crate::db::misc::pet::pet_db_search(f.item.nameid as i32, crate::db::misc::pet::PetItemType::Egg).is_some() {
        crate::pet::intif_delete_petdata(crate::pet::egg_pet_id(&f.item));
    }
    clif::clearflooritem(c, &u, None);
    c.db().flooritem().id().delete(id);
    c.db().unit().id().delete(id);
    unit::bump_mob_pos_gen();
}

// ---------------------------------------------------------------------------------------------- packets

/// clif.cpp clif_dropflooritem: ZC_ITEM_FALL_ENTRY5 to the area.
pub fn clif_dropflooritem(c: &Ctx, id: u32, can_show_effect: bool) {
    let Some(f) = c.db().flooritem().id().find(id) else { return };
    let Some(u) = unit::get(c, id) else { return };
    if f.item.nameid == 0 {
        return;
    }
    let (mut show, mut mode) = (0i8, 0i16);
    if can_show_effect {
        let drop_effect = itemdb::itemdb_dropeffect(f.item.nameid);
        if drop_effect > 0 {
            show = 1;
            mode = drop_effect as i16 - 1;
        } else if rndopt_drop_pillar() {
            let n = (0..MAX_ITEM_RDM_OPT).filter(|&i| f.item.opt(i).id != 0).count();
            if n > 0 {
                // DROPEFFECT_BLUE_PILLAR - 1, YELLOW - 1, PURPLE - 1 (PACKETVER >= 20200304: blue is 2)
                show = 1;
                mode = match n {
                    1 => 1,
                    2 => 2,
                    _ => 3,
                };
            }
        }
    }
    let p = packet_dropflooritem {
        PacketType: packet_dropflooritem::ID as i16,
        ITAID: f.id,
        ITID: itemdb::client_nameid(f.item.nameid),
        r#type: crate::clif_items::itemtype(f.item.nameid) as u16,
        IsIdentified: (f.item.identify != 0) as u8,
        xPos: u.x,
        yPos: u.y,
        subX: f.subx,
        subY: f.suby,
        count: f.item.amount,
        showdropeffect: show,
        dropeffectmode: mode,
    };
    clif::send(c, &p.to_vec(), &u, Target::Area);
}

/// clif.cpp clif_getareachar_item: ZC_ITEM_ENTRY to one player.
pub fn clif_getareachar_item(c: &Ctx, sd: &Unit, item: &Unit) {
    let Some(f) = c.db().flooritem().id().find(item.id) else { return };
    let p = PACKET_ZC_ITEM_ENTRY {
        packetType: PACKET_ZC_ITEM_ENTRY::ID as i16,
        AID: f.id,
        itemId: itemdb::client_nameid(f.item.nameid),
        identify: f.item.identify,
        x: item.x as u16,
        y: item.y as u16,
        amount: f.item.amount as u16,
        subX: f.subx,
        subY: f.suby,
    };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
}

/// clif.cpp clif_takeitem: ZC_NOTIFY_ACT DMG_PICKUP_ITEM to the area.
pub fn clif_takeitem(c: &Ctx, src: &Unit, dst_id: u32) {
    const DMG_PICKUP_ITEM: u8 = 1;
    let p = PACKET_ZC_NOTIFY_ACT { packetType: PACKET_ZC_NOTIFY_ACT::ID as i16, srcID: src.id as i32, targetID: dst_id as i32, r#type: DMG_PICKUP_ITEM, ..Default::default() };
    clif::send(c, &p.to_vec(), src, Target::Area);
}

// ---------------------------------------------------------------------------------------------- drop / take

/// pc.cpp pc_candrop.
pub fn pc_candrop(c: &Ctx, aid: u32, item: &Item) -> bool {
    if crate::status::sc::cant(c, aid, crate::status::consts::SCS_NODROPITEM) {
        return false;
    }
    if itemdb::itemdb_ishatched_egg(item.card0, item.attribute) {
        return false;
    }
    if pc::has_permission(c, aid, crate::rules::PC_PERM_TRADE_UNCONDITIONAL) {
        return true; // no restriction
    }
    if !pc_can_give_items(c, aid) {
        return false;
    }
    if item.expire_time != 0 || (item.bound != 0 && !pc_can_give_bounded_items(c, aid)) {
        return false;
    }
    itemdb::itemdb_isrestricted(item.nameid, &item.cards(), pc::group_level(c, aid), 0, itemdb::itemdb_isdropable_sub)
}

/// pc.cpp pc_dropitem.
pub fn pc_dropitem(c: &Ctx, aid: u32, n: i32, amount: i32) -> bool {
    if n < 0 || n as usize >= MAX_INVENTORY {
        return false;
    }
    if amount <= 0 {
        return false;
    }
    let Some(r) = inv_get(c, aid, n as usize) else { return false };
    let Some(p) = pc::get(c, aid) else { return false };
    let Some(sd) = unit::get(c, aid) else { return false };
    if r.item.nameid == 0 || r.item.amount <= 0 || (r.item.amount as i32) < amount || p.trading || p.vending {
        return false;
    }
    if r.item.equip_switch != 0 {
        return false;
    }
    if crate::npc::mapflag(c, sd.map, MF_NODROP) {
        clif::displaymessage(c, &sd, crate::data::msg_txt(271));
        return false; // Can't drop items in nodrop mapflag maps.
    }
    if !pc_candrop(c, aid, &r.item) {
        clif::displaymessage(c, &sd, crate::data::msg_txt(263));
        return false;
    }
    // Bypass drop restriction in map_addflooritem because we've already checked it above
    let kinds = if item_stacking() { 0 } else { BL_ITEM };
    if map_addflooritem(c, &r.item, amount, sd.map, sd.x, sd.y, 0, 0, 0, 2 | 4, 0, false, None, kinds) == 0 {
        return false;
    }
    pc_delitem(c, aid, n, amount, 1, 0, crate::map_log::LOG_TYPE_PICKDROP_PLAYER);
    crate::clif_items::dropitem(c, aid, n, amount);
    true
}

/// party.cpp party_share_loot (party.rs share_loot) with pc_additem(target, item, item->amount,
/// LOG_TYPE_PICKDROP_PLAYER) and clif_party_show_picker (party_show_share_picker, show_picker.item_type).
pub(crate) fn party_share_loot(c: &Ctx, party_id: u32, aid: u32, item: &mut Item, first_charid: u32) -> u8 {
    let amount = item.amount as i32;
    let mut it = item.clone();
    let (r, target) = crate::party::share_loot(c, party_id, aid, first_charid, &mut |t| pc_additem(c, t, &mut it, amount, crate::map_log::LOG_TYPE_PICKDROP_PLAYER) as i32);
    *item = it;
    if r != 0 {
        return r as u8;
    }
    if let Some(t) = target {
        if crate::party::show_picker_item_type() & (1 << itemdb::itemdb_type(item.nameid)) != 0 {
            crate::clif_items::party_show_picker(c, t, item);
        }
    }
    0
}

/// pc.cpp pc_takeitem.
pub fn pc_takeitem(c: &Ctx, aid: u32, fid: u32) -> bool {
    let tick = c.tick();
    let Some(sd) = unit::get(c, aid) else { return false };
    let Some(fu) = unit::get(c, fid) else { return false };
    let Some(f) = c.db().flooritem().id().find(fid) else { return false };
    const BS_GREED: u16 = 1013;
    if !crate::path::check_distance((fu.x - sd.x) as i32, (fu.y - sd.y) as i32, 3) && sd.ud.skill_id != BS_GREED {
        return false; // Distance is too far
    }
    if crate::status::sc::cant(c, aid, crate::status::consts::SCS_NOPICKITEM) {
        return false;
    }
    let char_id = sd.char_id;
    let party_id = crate::party::party_id_of(c, aid);
    let p = crate::party::party_search(c, party_id);
    let share = p.as_ref().map(|p| p.party.item & 1 != 0).unwrap_or(false);
    // Time the player needs to wait until the item can be taken
    let mut item_get_tick = f.third_get_tick;
    if f.first_get_charid == 0 || f.first_get_charid == char_id {
        // Top attacker or no attacker, no wait time
        item_get_tick = 0;
    } else if f.second_get_charid > 0 && f.second_get_charid == char_id && !share {
        // Second top attacker, needs to wait for top attacker
        item_get_tick = f.first_get_tick;
    } else if f.third_get_charid > 0 && f.third_get_charid == char_id && !share {
        // Third top attacker, needs to wait for top and second attacker
        item_get_tick = f.second_get_tick;
    } else if share {
        // Party member loot priority: only if the top attackers are still online
        let party_of = |cid: u32| if cid > 0 { crate::pc::charid2sd(c, cid).map(|u| crate::party::party_id_of(c, u.id)) } else { None };
        if party_of(f.first_get_charid) == Some(party_id) {
            item_get_tick = 0;
        } else if party_of(f.second_get_charid) == Some(party_id) {
            item_get_tick = f.first_get_tick;
        } else if party_of(f.third_get_charid) == Some(party_id) {
            item_get_tick = f.second_get_tick;
        }
    }
    // Cannot get item when wait time is still active
    if tick - item_get_tick < 0 {
        return false;
    }
    let mut it = f.item.clone();
    // This function takes care of giving the item to whoever should have it, considering party-share options.
    let flag = party_share_loot(c, party_id, aid, &mut it, f.first_get_charid);
    if flag != 0 {
        crate::clif_items::additem(c, aid, 0, 0, flag);
        return true;
    }
    // Display pickup animation.
    if let Some(mut u) = unit::get(c, aid) {
        unit::stop_attack(c, &mut u);
        unit::save(c, &u);
        clif_takeitem(c, &u, fid);
    }
    // (Somehow, if party's pickup distribution is 'Even Share', no announcemet)
    if f.mob_id != 0 && itemdb::itemdb_search(f.item.nameid).flag.broadcast && !p.as_ref().map(|p| p.party.item & 2 != 0).unwrap_or(false) {
        crate::atcmd_pc::intif_broadcast_obtain_special_item(c, aid, f.item.nameid, f.mob_id as u32, crate::atcmd_pc::ITEMOBTAIN_TYPE_MONSTER_ITEM);
    }
    map_clearflooritem(c, fid);
    true
}

// ---------------------------------------------------------------------------------------------- parse

/// clif.cpp clif_parse_DropItem.
pub fn parse_drop_item(c: &Ctx, aid: u32, pkt: &[u8]) {
    let pos = crate::net::packets::packet_pos(u16::from_le_bytes([pkt[0], pkt[1]]));
    let rd = |i: usize| -> i32 {
        let o = pos.get(i).copied().unwrap_or(0) as usize;
        pkt.get(o..o + 2).map(|b| u16::from_le_bytes([b[0], b[1]]) as i32).unwrap_or(0)
    };
    let item_index = rd(0) - 2;
    let item_amount = rd(1);
    let ok = (|| {
        let p = pc::get(c, aid)?;
        let sd = unit::get(c, aid)?;
        if p.dead_sit == 1 {
            return None;
        }
        if pc_cant_act2(&p, sd.sc.opt1) || p.npc_id != 0 {
            return None;
        }
        if crate::status::sc::cant(c, aid, crate::status::consts::SCS_NODROPITEM) {
            return None;
        }
        if !pc_dropitem(c, aid, item_index, item_amount) {
            return None;
        }
        if crate::conf::idletime_option() & 0x0100 != 0 {
            // IDLE_DROPITEM (pc.hpp: 0x0100)
            let mut p = pc::get(c, aid)?;
            p.idletime = crate::pc::last_tick(c);
            pc::put(c, &p);
        }
        Some(())
    })();
    if ok.is_none() {
        // Because the client does not like being ignored.
        crate::clif_items::dropitem(c, aid, item_index, 0);
    }
}

/// clif.cpp clif_parse_TakeItem.
pub fn parse_take_item(c: &Ctx, aid: u32, pkt: &[u8]) {
    let pos = crate::net::packets::packet_pos(u16::from_le_bytes([pkt[0], pkt[1]]));
    let o = pos.first().copied().unwrap_or(2) as usize;
    let map_object_id = pkt.get(o..o + 4).map(|b| u32::from_le_bytes([b[0], b[1], b[2], b[3]])).unwrap_or(0);
    let done = (|| {
        let p = pc::get(c, aid)?;
        let sd = unit::get(c, aid)?;
        if p.dead_sit == 1 {
            clif::clearunit_area(c, &sd, unit::CLR_DEAD);
            return None;
        }
        let fu = unit::get(c, map_object_id)?;
        if fu.kind != BL_ITEM || fu.map != sd.map {
            return None;
        }
        if p.npc_id != 0 || p.chat_id != 0 || pc_cant_act2(&p, sd.sc.opt1) {
            return None;
        }
        if !pc_takeitem(c, aid, map_object_id) {
            return None;
        }
        Some(())
    })();
    if done.is_none() {
        // Client REQUIRES a fail packet or you can no longer pick items.
        crate::clif_items::additem(c, aid, 0, 0, 6);
    }
}
