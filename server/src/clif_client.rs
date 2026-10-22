//! clif.cpp's remaining client requests (rAthena e985006, PACKETVER 20211103) that drive a system through its own
//! functions rather than an @command: /mineffect (LessEffect), the inventory's favorite tab (MoveItem), /pvpinfo, the
//! char-id name lookup (SolveCharName, with map.cpp's nick_db), cancelling an attack (StopAttack), viewing another
//! player's equipment, the world info, the inventory expansion window, the private airship and taking off every
//! equipment, plus the packets rAthena accepts and ignores (StoragePassword, client_version, dull).

use spacetimedb::{table, Table};

use crate::character::character as _;
use crate::clif::{self, Target};
use crate::inventory::{self, INVENTORY_BASE_SIZE, MAX_INVENTORY};
use crate::net::packets::*;
use crate::net::wire::{put_str, Reader};
use crate::unit::{self, Unit};
use crate::world::Ctx;
use crate::{pc, rules};

/// mmo.hpp NAME_LENGTH.
const NAME_LENGTH: usize = 24;

/// map_session_data fields these requests own (a row for a character that used one; it goes at unit_free).
#[table(accessor = pc_client)]
#[derive(Clone, Debug)]
pub struct PcClient {
    #[primary_key]
    pub account_id: u32,
    /// sd->state.lesseffect (CZ_LESSEFFECT). rAthena only stores it ("this state tracking was rendered useless").
    pub lesseffect: bool,
    /// sd->state.inventory_expansion_confirmation: the item the pending expansion uses (0: none). It holds only while
    /// `Pc::ui_open` has UI_INVENTORY_EXPANSION, the bit pc_cant_act2 tests and pc_setpos clears with the windows.
    pub inventory_expansion_confirmation: u32,
    /// sd->state.inventory_expansion_amount.
    pub inventory_expansion_amount: u16,
    /// The EMPTY inventory slots whose `struct item` holds favorite = 1: clif_parse_MoveItem writes the flag without
    /// looking whether the slot has an item (clif.cpp:19911-19914), and it stays until pc_additem copies an item
    /// into the slot (pc.cpp:6049 memcpy, 6057) or the session ends. (A slot with an item keeps the flag in its row.)
    pub empty_favorite: Vec<u16>,
}

/// `Pc::ui_open` bit: sd->state.inventory_expansion_confirmation != 0 (pc.hpp pc_cant_act2; pc.cpp:6957 pc_setpos
/// resets it). The bit owners are listed at pc.rs UI_BARTER.
pub const UI_INVENTORY_EXPANSION: u32 = 1 << 10;

/// map.cpp nick_db: the names of offline characters the char server sent this map server (map_addnickdb), by char id.
/// map_delnickdb drops a character's entry when it logs in (pc_reg_received).
#[table(accessor = nick_db)]
#[derive(Clone, Debug)]
pub struct NickDb {
    #[primary_key]
    pub char_id: u32,
    pub nick: String,
}

/// The requests' handlers, in clif.cpp's order; false when `func` is not one of them.
pub fn parse(c: &Ctx, aid: u32, func: &str, pkt: &[u8]) -> bool {
    match func {
        "clif_parse_StopAttack" => clif_parse_stop_attack(c, aid),
        "clif_parse_SolveCharName" => clif_parse_solve_char_name(c, aid, pkt),
        // CZ_ACK_STORE_PASSWORD (0x023b and the other ids bound to it): rAthena's handler is empty ("//@TODO": the
        // storage_changepass call is commented out, clif.cpp:13764-13772), so the packet is read and dropped
        "clif_parse_StoragePassword" => {}
        "clif_parse_PVPInfo" => clif_parse_pvp_info(c, aid),
        "clif_parse_ViewPlayerEquip" => clif_parse_view_player_equip(c, aid, pkt),
        "clif_parse_LessEffect" => clif_parse_less_effect(c, aid, pkt),
        "clif_parse_MoveItem" => clif_parse_move_item(c, aid, pkt),
        // clif.cpp:19972 clif_parse_dull: "placeholder for unsupported incoming packets (avoids server disconnecting
        // client)" — 0x08dd, CZ_CLOSE_MAILBOX 0x09e9, CZ_PING_LIVE 0x0b1c, 0x0b4c, 0x0b93 do nothing
        "clif_parse_dull" => {}
        "clif_parse_reqworldinfo" => clif_parse_reqworldinfo(c, aid),
        // CZ_CLIENT_VERSION (0x044a <version>.L): clif.cpp:20028 clif_parse_client_version does nothing (`//if(sd) ;`)
        "clif_parse_client_version" => {}
        "clif_parse_private_airship_request" => clif_parse_private_airship_request(c, aid, pkt),
        "clif_parse_unequipall" => clif_parse_unequipall(c, aid),
        "clif_parse_inventory_expansion_request" => clif_parse_inventory_expansion_request(c, aid),
        "clif_parse_inventory_expansion_confirm" => clif_parse_inventory_expansion_confirm(c, aid),
        "clif_parse_inventory_expansion_reject" => clif_parse_inventory_expansion_reject(c, aid),
        _ => return false,
    }
    true
}

// ---------------------------------------------------------------------------------------------- state

fn state(c: &Ctx, aid: u32) -> PcClient {
    c.db().pc_client().account_id().find(aid).unwrap_or(PcClient { account_id: aid, lesseffect: false, inventory_expansion_confirmation: 0, inventory_expansion_amount: 0, empty_favorite: Vec::new() })
}

fn put_state(c: &Ctx, s: PcClient) {
    if c.db().pc_client().account_id().find(s.account_id).is_some() {
        c.db().pc_client().account_id().update(s);
    } else {
        c.db().pc_client().insert(s);
    }
}

/// unit_free: the map_session_data goes, these fields with it.
pub fn unit_free(c: &Ctx, aid: u32) {
    c.db().pc_client().account_id().delete(aid);
}

fn send_self(c: &Ctx, aid: u32, buf: &[u8]) {
    if let Some(sd) = unit::get(c, aid) {
        clif::send(c, buf, &sd, Target::SelfOnly);
    }
}

/// The offset of the packet's field `i` (packet_db[RFIFOW(fd,0)].pos[i]).
fn pos(pkt: &[u8], i: usize) -> usize {
    let cmd = u16::from_le_bytes([pkt[0], pkt[1]]);
    packet_pos(cmd).get(i).copied().unwrap_or(2) as usize
}

// ---------------------------------------------------------------------------------------------- attack, names

/// clif.cpp clif_parse_StopAttack (CZ_CANCEL_LOCKON 0x0118): unit_stop_attack, and the attack no longer continues.
fn clif_parse_stop_attack(c: &Ctx, aid: u32) {
    let Some(mut u) = unit::get(c, aid) else { return };
    unit::stop_attack(c, &mut u);
    u.ud.attack_continue = false;
    unit::save(c, &u);
}

/// clif.cpp clif_parse_SolveCharName (CZ_REQNAME_BYGID 0x00a2 / 0x0369 / 0x0884 / 0x094c, the char id at pos[0]):
/// map_reqnickdb.
fn clif_parse_solve_char_name(c: &Ctx, aid: u32, pkt: &[u8]) {
    let charid = Reader::at(pkt, pos(pkt, 0)).i32().unwrap_or(0);
    map_reqnickdb(c, aid, charid);
}

/// map.cpp map_reqnickdb: the name of an online character, else the one nick_db holds (ZC_ACK_REQNAME_BYGID). For a
/// name nick_db does not hold, rAthena queues a charid_request and asks the char server (chrif_searchcharid; nothing
/// for char id 0); its answer (0x2b09: char_loadName's name, "" when there is no such character, PACKETVER >= 20180221)
/// reaches map_addnickdb, which stores the name and answers the queued requests — but map_reqnickdb never sets the
/// request's charid (map.cpp:2159-2161), so map_charid2sd(0) finds nobody to answer: the client gets no reply this
/// time, and its next request finds the name in nick_db. Here the character table answers at once in the char
/// server's place, with the same result.
pub fn map_reqnickdb(c: &Ctx, aid: u32, charid: i32) {
    let Some(sd) = unit::get(c, aid) else { return };
    if let Some(tsd) = pc::charid2sd(c, charid as u32) {
        let name = pc::character_of(c, tsd.id).map(|ch| ch.name).unwrap_or(tsd.name);
        crate::clif_items::solved_charname(c, &sd, charid as u32, &name);
        return;
    }
    if let Some(p) = c.db().nick_db().char_id().find(charid as u32) {
        if !p.nick.is_empty() {
            crate::clif_items::solved_charname(c, &sd, charid as u32, &p.nick);
            return;
        }
    }
    // not in cache, request it (chrif_searchcharid: `if( !char_id ) return -1;`)
    if charid != 0 {
        // char_loadName: SELECT `name` FROM `char` WHERE `char_id`='%d'
        let name = c.db().character().id().find(charid as u32).map(|ch| ch.name).unwrap_or_default();
        map_addnickdb(c, charid, &name);
    }
}

/// map.cpp map_addnickdb: an offline character's name goes into nick_db (safestrncpy NAME_LENGTH); the queued requests
/// it would answer carry char id 0 (map_reqnickdb) and reach nobody. (An entry with an empty name is kept by rAthena
/// but reads as missing: none is stored here.)
fn map_addnickdb(c: &Ctx, charid: i32, nick: &str) {
    if pc::charid2sd(c, charid as u32).is_some() {
        return; // already online
    }
    let nick: String = nick.chars().take(NAME_LENGTH - 1).collect();
    let char_id = charid as u32;
    if nick.is_empty() {
        return;
    }
    if c.db().nick_db().char_id().find(char_id).is_some() {
        c.db().nick_db().char_id().update(NickDb { char_id, nick });
    } else {
        c.db().nick_db().insert(NickDb { char_id, nick });
    }
}

/// map.cpp map_delnickdb (pc_reg_received: the character logs in): its nick_db entry goes; the queued requests it
/// answers carry char id 0 and reach nobody (map_reqnickdb).
pub fn map_delnickdb(c: &Ctx, char_id: u32) {
    c.db().nick_db().char_id().delete(char_id);
}

// ---------------------------------------------------------------------------------------------- pvp, equipment

/// clif.cpp clif_parse_PVPInfo (CZ_REQ_PVPPOINT 0x020f: /pvpinfo; the char / account id fields are not read):
/// clif_PVPInfo.
fn clif_parse_pvp_info(c: &Ctx, aid: u32) {
    clif_pvpinfo(c, aid);
}

/// clif.cpp clif_PVPInfo: ZC_ACK_PVPPOINT (0x0210 <char id>.L <account id>.L <win point>.L <lose point>.L <point>.L).
fn clif_pvpinfo(c: &Ctx, aid: u32) {
    let Some(p) = pc::get(c, aid) else { return };
    let b = crate::battle::state::get(c, aid);
    let mut w = Vec::with_capacity(22);
    w.extend_from_slice(&0x0210u16.to_le_bytes());
    w.extend_from_slice(&p.char_id.to_le_bytes());
    w.extend_from_slice(&aid.to_le_bytes());
    w.extend_from_slice(&b.pvp_won.to_le_bytes()); // times won
    w.extend_from_slice(&b.pvp_lost.to_le_bytes()); // times lost
    w.extend_from_slice(&b.pvp_point.to_le_bytes());
    send_self(c, aid, &w);
}

/// clif.hpp MSI_OPEN_EQUIPEDITEM_REFUSED: "This character's equipment information is not open to the public."
const MSI_OPEN_EQUIPEDITEM_REFUSED: u16 = 1357;

/// clif.cpp clif_parse_ViewPlayerEquip (CZ_EQUIPWIN_MICROSCOPE 0x02d6 <account id>.L): the equipment of a player on
/// the same map when it shows it (/equip: show_equip) or the viewer has PC_PERM_VIEW_EQUIPMENT; else the refusal.
fn clif_parse_view_player_equip(c: &Ctx, aid: u32, pkt: &[u8]) {
    let tid = Reader::at(pkt, pos(pkt, 0)).i32().unwrap_or(0);
    // map_id2sd
    if tid <= 0 || pc::get(c, tid as u32).is_none() {
        return;
    }
    let (Some(sd), Some(tsd)) = (unit::get(c, aid), unit::get(c, tid as u32)) else { return };
    if sd.map != tsd.map {
        return;
    }
    let show_equip = pc::character_of(c, tsd.id).map(|ch| ch.show_equip != 0).unwrap_or(false);
    if show_equip || pc::has_permission(c, aid, rules::PC_PERM_VIEW_EQUIPMENT) {
        clif_viewequip_ack(c, &sd, &tsd);
    } else {
        crate::clif_items::msg(c, &sd, MSI_OPEN_EQUIPEDITEM_REFUSED);
    }
}

/// clif.cpp clif_viewequip_ack: ZC_EQUIPWIN_MICROSCOPE with `tsd`'s name and looks and, for each equip slot in
/// EQI order, the worn equipment there (an item worn in several slots — a two-handed weapon, a multi-slot headgear —
/// is listed once per slot, as rAthena does).
fn clif_viewequip_ack(c: &Ctx, sd: &Unit, tsd: &Unit) {
    use crate::db::jobs::ids::{JOB_SECOND_JOB_END, JOB_SECOND_JOB_START};
    let vd = &tsd.vd;
    let name = pc::character_of(c, tsd.id).map(|ch| ch.name).unwrap_or_else(|| tsd.name.clone());
    let mut list = Vec::new();
    let equip_index = inventory::st(c, tsd.id).map(|s| s.equip_index).unwrap_or_default();
    for &k in equip_index.iter().take(inventory::EQI_MAX) {
        if k < 0 {
            continue;
        }
        let Some(r) = inventory::inv_get(c, tsd.id, k as usize) else { continue }; // Item doesn't exist
        let Some(id) = crate::db::items::itemdb_exists(r.item.nameid) else { continue };
        if r.item.equip == 0 {
            continue;
        }
        if !crate::db::items::itemdb_isequip2(id) {
            continue; // Is not equippable
        }
        let eqp = crate::pc_equip::pc_equippoint(c, tsd.id, &r.item);
        list.push(crate::clif_items::item_equip(crate::clif_items::client_index(k as usize), &r.item, id, eqp));
    }
    let p = PACKET_ZC_EQUIPWIN_MICROSCOPE {
        PacketType: PACKET_ZC_EQUIPWIN_MICROSCOPE::ID as i16,
        PacketLength: 0,
        characterName: name,
        job: vd.class as i16,
        head: vd.hair as i16,
        accessory: vd.head_bottom as i16,
        accessory2: vd.head_mid as i16,
        accessory3: vd.head_top as i16,
        robe: vd.robe as i16,
        headpalette: vd.hair_color as i16,
        bodypalette: vd.clothes_color as i16,
        // PACKETVER_RE_NUM >= 20180801: 1 for a second-job costume body
        body2: (vd.body2 > JOB_SECOND_JOB_START && vd.body2 < JOB_SECOND_JOB_END) as i16,
        sex: vd.sex,
        list,
    };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
}

// ---------------------------------------------------------------------------------------------- client settings

/// clif.cpp clif_parse_LessEffect (CZ_LESSEFFECT 0x021d: /mineffect): sd->state.lesseffect.
fn clif_parse_less_effect(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_LESSEFFECT::read(&mut Reader::new(pkt)) else { return };
    let mut s = state(c, aid);
    s.lesseffect = p.state != 0;
    put_state(c, s);
}

/// clif.cpp clif_parse_MoveItem (CZ_INVENTORY_TAB 0x0907 <index>.W <favorite>.B): an item moves to the favorite tab
/// (favorite 0) or back (1), then clif_favorite_item. The byte is a C++ bool: read as nonzero. rAthena does not look
/// whether the slot holds an item: an empty slot's `struct item` takes the flag too (`PcClient::empty_favorite`), so
/// a second request on the same empty slot sees it (parity: testrig/parity/ggm client_inv).
fn clif_parse_move_item(c: &Ctx, aid: u32, pkt: &[u8]) {
    /* can't move while dead. */
    if pc::isdead(c, aid) {
        return;
    }
    let Some(p) = PACKET_CZ_INVENTORY_TAB::read(&mut Reader::new(pkt)) else { return };
    let index = crate::itemui::server_index(p.index as u16) as usize;
    if index >= MAX_INVENTORY {
        return;
    }
    let favorite = p.favorite != 0;
    let row = inventory::inv_get(c, aid, index);
    let current = match &row {
        Some(r) => r.item.favorite,
        None => state(c, aid).empty_favorite.contains(&(index as u16)) as u8,
    };
    let new = if current != 0 && favorite {
        0
    } else if !favorite {
        1
    } else {
        return; /* nothing to do. */
    };
    match row {
        Some(mut r) => {
            r.item.favorite = new;
            inventory::inv_put(c, &r);
        }
        None => {
            let mut s = state(c, aid);
            s.empty_favorite.retain(|&i| i != index as u16);
            if new != 0 {
                s.empty_favorite.push(index as u16);
            }
            put_state(c, s);
        }
    }
    clif_favorite_item(c, aid, index, new);
}

/// pc.cpp pc_additem putting an item into the blank slot `index` (pc.cpp:6049 memcpy of the new item over the slot,
/// 6057 `favorite = favorite`): a favorite flag clif_parse_MoveItem left on the empty slot is gone.
pub fn slot_filled(c: &Ctx, aid: u32, index: usize) {
    let Some(mut s) = c.db().pc_client().account_id().find(aid) else { return };
    if s.empty_favorite.contains(&(index as u16)) {
        s.empty_favorite.retain(|&i| i != index as u16);
        c.db().pc_client().account_id().update(s);
    }
}

/// clif.cpp clif_favorite_item: ZC_INVENTORY_TAB (favorite 1 is the favorite tab, sent as false).
fn clif_favorite_item(c: &Ctx, aid: u32, index: usize, favorite: u8) {
    let p = PACKET_ZC_INVENTORY_TAB { packetType: PACKET_ZC_INVENTORY_TAB::ID as i16, index: crate::clif_items::client_index(index) as i16, favorite: (favorite != 1) as u8 };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_parse_reqworldinfo (CZ_REQ_BEFORE_WORLD_INFO 0x0978; the account id is not trusted): clif_ackworldinfo.
fn clif_parse_reqworldinfo(c: &Ctx, aid: u32) {
    clif_ackworldinfo(c, aid);
}

/// clif.cpp clif_ackworldinfo: ZC_ACK_BEFORE_WORLD_INFO (0x0979 <world name>.24B <char name>.24B) with an empty world
/// name.
fn clif_ackworldinfo(c: &Ctx, aid: u32) {
    let name = pc::character_of(c, aid).map(|ch| ch.name).unwrap_or_default();
    let mut w = Vec::with_capacity(50);
    w.extend_from_slice(&0x0979u16.to_le_bytes());
    put_str(&mut w, "" /* World name */, 24);
    put_str(&mut w, &name, NAME_LENGTH);
    send_self(c, aid, &w);
}

// ---------------------------------------------------------------------------------------------- private airship

/// conf/battle/feature.conf:122 `feature.privateairship: on` (battle.cpp:9115-9120 turns it off only for PACKETVER <
/// 20180321).
fn feature_privateairship() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::FEATURE_PRIVATEAIRSHIP) != 0
}
/// map.hpp e_mapflag MF_PRIVATEAIRSHIP_SOURCE / MF_PRIVATEAIRSHIP_DESTINATION.
const MF_PRIVATEAIRSHIP_SOURCE: i16 = 67;
const MF_PRIVATEAIRSHIP_DESTINATION: i16 = 68;
/// itemdb.hpp IG_PRIVATE_AIRSHIP (script constant).
const IG_PRIVATE_AIRSHIP: u16 = 489;
/// mmo.hpp MAP_NAME_LENGTH_EXT.
const MAP_NAME_LENGTH_EXT: usize = 16;

/// clif.cpp e_private_airship_response.
const PRIVATEAIRSHIP_RETRY: u32 = 1;
const PRIVATEAIRSHIP_ITEM_NOT_ENOUGH: u32 = 2;
const PRIVATEAIRSHIP_DESTINATION_MAP_INVALID: u32 = 3;
const PRIVATEAIRSHIP_SOURCE_MAP_INVALID: u32 = 4;
const PRIVATEAIRSHIP_ITEM_UNAVAILABLE: u32 = 5;

/// clif.cpp clif_private_airship_response: ZC_PRIVATE_AIRSHIP_RESPONSE (0x0a4a <response>.L).
fn clif_private_airship_response(c: &Ctx, aid: u32, response: u32) {
    let p = PACKET_ZC_PRIVATE_AIRSHIP_RESPONSE { PacketType: 0x0a4a, flag: response };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_parse_private_airship_request (CZ_PRIVATE_AIRSHIP_REQUEST 0x0a49 <map name>.16B <item id>.L): from a
/// PrivateAirship_Source map to a PrivateAirship_Destination one, paying one item of the PRIVATE_AIRSHIP group, to a
/// random cell (pc_setpos 0,0 CLR_TELEPORT). An unknown map (mapindex 0, then no map id), the player's own map and an
/// instance map end the request without an answer.
fn clif_parse_private_airship_request(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(sd) = unit::get(c, aid) else { return };
    // Check if the feature is enabled
    if !feature_privateairship() {
        let msg = crate::net::wire::latin1(crate::data::msg_txt(792)); // The private airship system is disabled.
        clif::messagecolor_target(c, &sd, crate::social::COLOR_RED, &msg, false, Target::SelfOnly, None);
        return;
    }
    // Check if the player is allowed to warp from the source map
    if !crate::npc::mapflag(c, sd.map, MF_PRIVATEAIRSHIP_SOURCE) {
        clif_private_airship_response(c, aid, PRIVATEAIRSHIP_SOURCE_MAP_INVALID);
        return;
    }
    let Some(p) = PACKET_CZ_PRIVATE_AIRSHIP_REQUEST::read(&mut Reader::new(pkt)) else { return };
    let mapname: String = p.mapName.chars().take(MAP_NAME_LENGTH_EXT - 1).collect();
    let mapindex = crate::pc_taekwon::mapindex_name2id(c, &mapname) as i16;
    // Check if we know the mapname (mapindex_name2id returns 0, not a negative index, for an unknown name)
    if mapindex < 0 {
        clif_private_airship_response(c, aid, PRIVATEAIRSHIP_DESTINATION_MAP_INVALID);
        return;
    }
    let mapid = crate::pc_taekwon::map_mapindex2mapid(c, mapindex as u16) as i16;
    // Check if the map is available on this server
    if mapid < 0 {
        // TODO: add multi map-server support, cant validate the mapflags of the other server
        return;
    }
    // Check if the player tried to warp to the map he is on
    if sd.map as i16 == mapid {
        // This is blocked by the client, but just to be sure
        return;
    }
    // This can only be a hack, so we prevent it
    if crate::instance_map::map_instance_id(c, mapid as u16) != 0 {
        // Ignore requests to warp directly into a running instance
        return;
    }
    // Check if the player is allowed to warp to the target map
    if !crate::npc::mapflag(c, mapid as u16, MF_PRIVATEAIRSHIP_DESTINATION) {
        clif_private_airship_response(c, aid, PRIVATEAIRSHIP_DESTINATION_MAP_INVALID);
        return;
    }
    let item_id = p.ItemID;
    // Check if the item sent by the client is known to us
    if !crate::db::items::group::itemdb_group_item_exists(IG_PRIVATE_AIRSHIP, item_id) {
        clif_private_airship_response(c, aid, PRIVATEAIRSHIP_ITEM_UNAVAILABLE);
        return;
    }
    let idx = inventory::pc_search_inventory(c, aid, item_id);
    // Check if the player has the item at all
    if idx < 0 {
        clif_private_airship_response(c, aid, PRIVATEAIRSHIP_ITEM_NOT_ENOUGH);
        return;
    }
    // Delete the chosen item
    if inventory::pc_delitem(c, aid, idx as i32, 1, 0, 0, crate::map_log::LOG_TYPE_PRIVATE_AIRSHIP) != 0 {
        clif_private_airship_response(c, aid, PRIVATEAIRSHIP_RETRY);
        return;
    }
    // Warp the player to a random spot on the destination map
    pc::setpos(c, aid, mapid as u16, 0, 0, unit::CLR_TELEPORT);
}

// ---------------------------------------------------------------------------------------------- take off everything

/// clif.cpp clif_parse_unequipall (CZ_REQ_TAKEOFF_EQUIP_ALL 0x0bad; PACKETVER_RE_NUM >= 20211103): refused while the
/// player cannot act, else every equipment of the slots before EQI_COSTUME_HEAD_TOP comes off (pc_unequipitem flag 1;
/// the location mask is all bits before PACKETVER_MAIN_NUM 20230906), then ZC_ACK_TAKEOFF_EQUIP_ALL.
fn clif_parse_unequipall(c: &Ctx, aid: u32) {
    let (Some(p), Some(u)) = (pc::get(c, aid), unit::get(c, aid)) else { return };
    if pc::cant_act(&p, u.sc.opt1) {
        clif_unequipall_reply(c, aid, true);
        return;
    }
    let location: u32 = 0xFFFF_FFFF;
    let max = inventory::EQI_COSTUME_HEAD_TOP;
    for i in 0..max {
        // sd->equip_index[i] as it is now (a two-handed weapon leaves both hands at once)
        let n = inventory::st(c, aid).and_then(|s| s.equip_index.get(i).copied()).unwrap_or(-1);
        if n >= 0 && location & inventory::EQUIP_BITMASK[i] != 0 {
            crate::pc_equip::pc_unequipitem(c, aid, n as i32, 1);
        }
    }
    clif_unequipall_reply(c, aid, false);
}

/// clif.cpp clif_unequipall_reply: ZC_ACK_TAKEOFF_EQUIP_ALL.
fn clif_unequipall_reply(c: &Ctx, aid: u32, failed: bool) {
    let p = PACKET_ZC_ACK_TAKEOFF_EQUIP_ALL { PacketType: PACKET_ZC_ACK_TAKEOFF_EQUIP_ALL::ID as i16, result: failed as u8 };
    send_self(c, aid, &p.to_vec());
}

// ---------------------------------------------------------------------------------------------- inventory expansion

/// itemdb.hpp ITEMID_INVENTORY_EX_EVT / _DIS / ITEMID_INVENTORY_EX, each worth 10 slots. rAthena keeps them in a
/// std::map, so they are tried in ascending id order (the comment's "order of entries in this list" is this one).
const EXPANSION_ITEMS: [(u32, u16); 3] = [(25791, 10), (25792, 10), (25793, 10)];

/// clif.cpp e_inventory_expansion_response / e_inventory_expansion_result (ASK_CONFIRMATION / SUCCESS = 0).
const EXPANSION_OK: u8 = 0;
const EXPANSION_FAILED: u8 = 1;
const EXPANSION_BUSY: u8 = 2;
const EXPANSION_MISSING_ITEM: u8 = 3;
const EXPANSION_MAXIMUM_REACHED: u8 = 4;

/// (sd->state.inventory_expansion_confirmation, sd->state.inventory_expansion_amount).
fn expansion_state(c: &Ctx, aid: u32) -> (u32, u16) {
    let open = pc::get(c, aid).map(|p| p.ui_open & UI_INVENTORY_EXPANSION != 0).unwrap_or(false);
    let s = state(c, aid);
    (if open { s.inventory_expansion_confirmation } else { 0 }, s.inventory_expansion_amount)
}

fn set_expansion_state(c: &Ctx, aid: u32, item: u32, amount: u16) {
    let mut s = state(c, aid);
    s.inventory_expansion_confirmation = item;
    s.inventory_expansion_amount = amount;
    put_state(c, s);
    if let Some(mut p) = pc::get(c, aid) {
        if item != 0 {
            p.ui_open |= UI_INVENTORY_EXPANSION;
        } else {
            p.ui_open &= !UI_INVENTORY_EXPANSION;
        }
        pc::put(c, &p);
    }
}

fn inventory_slots(c: &Ctx, aid: u32) -> i32 {
    inventory::status(c, aid).map(|ch| ch.inventory_slots).unwrap_or(INVENTORY_BASE_SIZE) as i32
}

/// clif.cpp clif_inventory_expansion_info: ZC_EXTEND_BODYITEM_SIZE (the slots above INVENTORY_BASE_SIZE).
fn clif_inventory_expansion_info(c: &Ctx, aid: u32) {
    let p = PACKET_ZC_EXTEND_BODYITEM_SIZE { packetType: PACKET_ZC_EXTEND_BODYITEM_SIZE::ID as i16, expansionSize: (inventory_slots(c, aid) - INVENTORY_BASE_SIZE as i32) as i16 };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_inventory_expansion_response: ZC_ACK_OPEN_MSGBOX_EXTEND_BODYITEM_SIZE with the pending item.
fn clif_inventory_expansion_response(c: &Ctx, aid: u32, response: u8) {
    let p = PACKET_ZC_ACK_OPEN_MSGBOX_EXTEND_BODYITEM_SIZE { packetType: PACKET_ZC_ACK_OPEN_MSGBOX_EXTEND_BODYITEM_SIZE::ID as i16, result: response, itemId: expansion_state(c, aid).0 };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_inventory_expansion_result: ZC_ACK_EXTEND_BODYITEM_SIZE, and the state tracking is reset.
fn clif_inventory_expansion_result(c: &Ctx, aid: u32, result: u8) {
    let p = PACKET_ZC_ACK_EXTEND_BODYITEM_SIZE { packetType: PACKET_ZC_ACK_EXTEND_BODYITEM_SIZE::ID as i16, result };
    send_self(c, aid, &p.to_vec());
    // Reset the state tracking
    set_expansion_state(c, aid, 0, 0);
}

/// clif.cpp clif_parse_inventory_expansion_request (CZ_REQ_OPEN_MSGBOX_EXTEND_BODYITEM_SIZE 0x0b14): picks the first
/// expansion item the player has that still fits under MAX_INVENTORY and asks for confirmation. Quirk kept: when the
/// only items found would pass the maximum, no item is picked and the answer is MISSING_ITEM, not MAXIMUM_REACHED.
fn clif_parse_inventory_expansion_request(c: &Ctx, aid: u32) {
    let (Some(p), Some(u)) = (pc::get(c, aid), unit::get(c, aid)) else { return };
    // Check if player is dead or busy with other stuff
    if pc::isdead(c, aid) || pc::cant_act(&p, u.sc.opt1) {
        clif_inventory_expansion_response(c, aid, EXPANSION_BUSY);
        return;
    }
    let slots = inventory_slots(c, aid);
    // Check if the player already reached the maximum
    if slots >= MAX_INVENTORY as i32 {
        clif_inventory_expansion_response(c, aid, EXPANSION_MAXIMUM_REACHED);
        return;
    }
    let mut found_over_limit = false;
    for (item, amount) in EXPANSION_ITEMS {
        // Check if the player has the required item
        let index = inventory::pc_search_inventory(c, aid, item);
        // Found an item
        if index >= 0 {
            // Check if the player would exceed the maximum
            if slots + amount as i32 > MAX_INVENTORY as i32 {
                found_over_limit = true;
            } else {
                found_over_limit = false;
                set_expansion_state(c, aid, item, amount);
                break;
            }
        }
    }
    // Check if an item was found
    if expansion_state(c, aid).0 == 0 {
        clif_inventory_expansion_response(c, aid, EXPANSION_MISSING_ITEM);
        return;
    }
    // Check if an item would have been found, but the player would exceed the maximum
    if found_over_limit {
        clif_inventory_expansion_response(c, aid, EXPANSION_MAXIMUM_REACHED);
        return;
    }
    // The player met all requirements => ask him for confirmation
    clif_inventory_expansion_response(c, aid, EXPANSION_OK);
}

/// clif.cpp clif_parse_inventory_expansion_confirm (CZ_REQ_EXTEND_BODYITEM_SIZE 0x0b16): the pending item is used up
/// and the inventory grows (saved at once: chrif_save CSAVE_NORMAL | CSAVE_INVENTORY), then the result and the new size.
fn clif_parse_inventory_expansion_confirm(c: &Ctx, aid: u32) {
    let (item, amount) = expansion_state(c, aid);
    if item == 0 {
        return;
    }
    // Check if player is dead
    if pc::isdead(c, aid) {
        clif_inventory_expansion_result(c, aid, EXPANSION_BUSY);
        return;
    }
    // Check if the player already reached the maximum
    if inventory_slots(c, aid) >= MAX_INVENTORY as i32 {
        clif_inventory_expansion_result(c, aid, EXPANSION_MAXIMUM_REACHED);
        return;
    }
    // Check if the player has the required item
    let index = inventory::pc_search_inventory(c, aid, item);
    // The player did not have the item anymore
    if index < 0 {
        clif_inventory_expansion_result(c, aid, EXPANSION_MISSING_ITEM);
        return;
    }
    // Check if the player would exceed the maximum
    if inventory_slots(c, aid) + amount as i32 > MAX_INVENTORY as i32 {
        clif_inventory_expansion_result(c, aid, EXPANSION_MAXIMUM_REACHED);
        return;
    }
    // Delete the required item
    if inventory::pc_delitem(c, aid, index as i32, 1, 0, 0, crate::map_log::LOG_TYPE_OTHER) != 0 {
        clif_inventory_expansion_result(c, aid, EXPANSION_FAILED);
        return;
    }
    // Increase the slots (the character row is read again: pc_delitem may have written it)
    if let Some(mut ch) = inventory::status(c, aid) {
        ch.inventory_slots = ch.inventory_slots.wrapping_add(amount as i16);
        inventory::put_status(c, &ch);
    }
    // Save player data (slots) and inventory data (removed item)
    crate::chrif::chrif_save(c, aid, crate::chrif::CSAVE_NORMAL | crate::chrif::CSAVE_INVENTORY);
    // Inform the player of success
    clif_inventory_expansion_result(c, aid, EXPANSION_OK);
    clif_inventory_expansion_info(c, aid);
}

/// clif.cpp clif_parse_inventory_expansion_reject (CZ_CLOSE_MSGBOX_EXTEND_BODYITEM_SIZE 0x0b19): the pending
/// expansion is dropped, nothing is sent.
fn clif_parse_inventory_expansion_reject(c: &Ctx, aid: u32) {
    set_expansion_state(c, aid, 0, 0);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn packet_offsets() {
        // SolveCharName: the char id's offset differs per packet id (clif_packetdb.hpp for this PACKETVER)
        for (cmd, off) in [(0x00a2u16, 10usize), (0x0369, 2), (0x0884, 2), (0x094c, 2)] {
            assert_eq!(crate::net::packets::parser_name(cmd), "clif_parse_SolveCharName");
            let mut p = cmd.to_le_bytes().to_vec();
            p.resize(crate::net::packets::packet_len(cmd).unwrap_or(0) as usize, 0);
            assert_eq!(pos(&p, 0), off, "{cmd:#06x}");
        }
        assert_eq!(pos(&[0xd6, 0x02, 0, 0, 0, 0], 0), 2); // ViewPlayerEquip
    }

    #[test]
    fn expansion_items_and_bits() {
        assert_eq!(crate::data::constant("IG_PRIVATE_AIRSHIP"), Some(IG_PRIVATE_AIRSHIP as i64));
        // db/re/item_group_db.yml PRIVATE_AIRSHIP: Nyangvine_Fruit (6909), World_Tour_Ticket (25464)
        use crate::db::items::group::itemdb_group_item_exists;
        assert!(itemdb_group_item_exists(IG_PRIVATE_AIRSHIP, 6909) && itemdb_group_item_exists(IG_PRIVATE_AIRSHIP, 25464));
        assert!(!itemdb_group_item_exists(IG_PRIVATE_AIRSHIP, 501));
        // ascending ids, as rAthena's std::map iterates them
        assert!(EXPANSION_ITEMS.windows(2).all(|w| w[0].0 < w[1].0));
        for (id, _) in EXPANSION_ITEMS {
            assert!(crate::db::items::itemdb_exists(id).is_some(), "{id}");
        }
        assert_eq!(crate::db::items::itemdb_exists(25791).map(|d| d.name.as_str()), Some("Inventory_Ex_Evt"));
        assert_eq!(crate::db::items::itemdb_exists(25793).map(|d| d.name.as_str()), Some("Inventory_Ex"));
        // the item windows' bits, barter's and roulette's leave this one free (pc.rs ui_open_bits checks them all)
        assert_eq!(UI_INVENTORY_EXPANSION & (crate::itemui::UI_ITEMUI_MASK | crate::pc::UI_BARTER | crate::pc::UI_BARTER_EXTENDED | crate::roulette::UI_ROULETTE), 0);
    }
}
