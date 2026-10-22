//! map/chat.cpp: chat rooms (players' rooms and NPC waiting rooms) and their packets (clif.cpp clif_createchat,
//! clif_dispchat, clif_joinchatok, …, clif_parse_CreateChatRoom …). A room is a block_list object with an id from
//! map_get_new_object_id; here it is a `chat_room` row (it is never drawn, so it needs no `unit` row). A player's
//! sd->chatID is `pc.chat_id`; an NPC's nd->chat_id is found by the room's owner.

use spacetimedb::{table, Table};

use crate::clif::{self, Target};
use crate::map::{BL_NPC, BL_PC};
use crate::net::packets::*;
use crate::net::wire::{from_latin1, latin1, Reader};
use crate::pc;
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// chat.hpp MAX_CHAT_USERS.
pub const MAX_CHAT_USERS: usize = 20;
/// map.hpp CHATROOM_TITLE_SIZE (36 + 1) and CHATROOM_PASS_SIZE (8 + 1).
pub const CHATROOM_TITLE_SIZE: usize = 37;
pub const CHATROOM_PASS_SIZE: usize = 9;
/// map.hpp MAX_LEVEL; mmo.hpp MAX_ZENY.
pub const MAX_LEVEL: u32 = 275;
pub const MAX_ZENY: u32 = i32::MAX as u32;
/// map.hpp MF_NOCHAT (script_constants: 43).
pub const MF_NOCHAT: i16 = 43;
/// player.conf `min_npc_vendchat_distance: 3`.
pub fn min_npc_vendchat_distance() -> i16 {
    crate::battle_config::get(crate::battle_config::Bc::MIN_NPC_VENDCHAT_DISTANCE) as i16
}

/// clif.hpp e_create_chatroom / e_refuse_enter_room / e_status_chatroom.
pub const CREATEROOM_SUCCESS: u8 = 0;
pub const CREATEROOM_LIMIT_EXCEEDED: u8 = 1;
pub const ENTERROOM_FULL: u8 = 0;
pub const ENTERROOM_WRONG_PASSWORD: u8 = 1;
pub const ENTERROOM_KICKED: u8 = 2;
pub const ENTERROOM_NO_ZENY: u8 = 4;
pub const ENTERROOM_TOO_LOW_LEVEL: u8 = 5;
pub const ENTERROOM_TOO_HIGH_LEVEL: u8 = 6;
pub const STATUSROOM_PRIVATE: u8 = 0;
pub const STATUSROOM_PUBLIC: u8 = 1;
pub const STATUSROOM_ARENA: u8 = 2;
pub const STATUSROOM_PK_ZONE: u8 = 3;

/// chat.hpp struct chat_data.
#[table(accessor = chat_room)]
#[derive(Clone, Debug)]
pub struct ChatRoom {
    #[primary_key]
    pub id: u32,
    /// Room title / password bytes (without the NUL).
    pub title: Vec<u8>,
    pub pass: Vec<u8>,
    pub is_pub: bool,
    /// usersd[]: the players in the room, in order (the first is a player room's owner).
    pub users: Vec<u32>,
    /// Join limit (uint8).
    pub limit: u8,
    /// Number of users needed to trigger the event (uint8; 0x80 = event disabled).
    pub trigger: u8,
    pub zeny: u32,
    pub min_lvl: u32,
    pub max_lvl: u32,
    /// The owner block: a player (account id) or an NPC.
    #[index(btree)]
    pub owner: u32,
    pub owner_is_npc: bool,
    pub npc_event: String,
    /// kick_list (char ids) of a player's room.
    pub kick_list: Vec<u32>,
    pub m: u16,
    pub x: i16,
    pub y: i16,
}

pub fn get(c: &Ctx, id: u32) -> Option<ChatRoom> {
    if id == 0 {
        return None;
    }
    c.db().chat_room().id().find(id)
}

fn put(c: &Ctx, cd: &ChatRoom) {
    c.db().chat_room().id().update(cd.clone());
}

/// nd->chat_id: the waiting room of this NPC (0 = none).
pub fn npc_chat_id(c: &Ctx, npc_id: u32) -> u32 {
    c.db().chat_room().owner().filter(npc_id).find(|cd| cd.owner_is_npc).map(|cd| cd.id).unwrap_or(0)
}

/// pc_setchatid.
fn pc_setchatid(c: &Ctx, aid: u32, id: u32) {
    if let Some(mut p) = pc::get(c, aid) {
        if p.chat_id != id {
            p.chat_id = id;
            pc::put(c, &p);
        }
    }
}

fn chat_id_of(c: &Ctx, aid: u32) -> u32 {
    pc::get(c, aid).map(|p| p.chat_id).unwrap_or(0)
}

fn owner_unit(c: &Ctx, cd: &ChatRoom) -> Option<Unit> {
    unit::get(c, cd.owner)
}

/// clif.cpp clif_send CHAT / CHAT_WOS: `src` is a player (its room) or the room itself.
pub enum ChatSrc {
    Pc(u32),
    Room(u32),
}

pub fn send(c: &Ctx, buf: &[u8], src: ChatSrc, wos: bool) {
    let (cd, sd) = match src {
        ChatSrc::Pc(aid) => (get(c, chat_id_of(c, aid)), aid),
        ChatSrc::Room(id) => (get(c, id), 0),
    };
    let Some(cd) = cd else { return };
    for &u in cd.users.iter() {
        if wos && u == sd {
            continue;
        }
        if let Some(p) = pc::get(c, u).filter(|p| !p.disconnected) {
            c.send(p.identity, buf);
        }
    }
}

// ---------------------------------------------------------------------------------------------- chat.cpp

/// `cd->m` of a room whose owner is on no map (a floating NPC: bl->m == -1, here as the int16's bits).
pub const CHAT_NO_MAP: u16 = u16::MAX;

/// chat.cpp chat_createchat: a new room owned by `owner` (common to player and NPC rooms).
#[allow(clippy::too_many_arguments)]
fn chat_createchat(c: &Ctx, owner: &Unit, title: &[u8], pass: &[u8], limit: i32, is_pub: bool, trigger: i32, ev: &str, zeny: i32, min_lvl: i32, max_lvl: i32) -> Option<ChatRoom> {
    chat_createchat_at(c, owner.id, owner.kind == BL_NPC, (owner.map, owner.x, owner.y), title, pass, limit, is_pub, trigger, ev, zeny, min_lvl, max_lvl)
}

/// chat_createchat for an owner block given by its id and position (`cd->owner = bl; cd->m = bl->m; …`).
#[allow(clippy::too_many_arguments)]
fn chat_createchat_at(c: &Ctx, owner_id: u32, owner_is_npc: bool, pos: (u16, i16, i16), title: &[u8], pass: &[u8], limit: i32, is_pub: bool, trigger: i32, ev: &str, zeny: i32, min_lvl: i32, max_lvl: i32) -> Option<ChatRoom> {
    let id = crate::map::get_new_object_id(c);
    if id == 0 {
        return None;
    }
    let cd = ChatRoom {
        id,
        title: title.iter().copied().take_while(|&b| b != 0).take(CHATROOM_TITLE_SIZE - 1).collect(),
        pass: pass.iter().copied().take_while(|&b| b != 0).take(CHATROOM_PASS_SIZE - 1).collect(),
        is_pub,
        users: Vec::new(),
        limit: limit.min(MAX_CHAT_USERS as i32) as u8,
        trigger: trigger as u8,
        zeny: zeny as u32,
        min_lvl: min_lvl as u32,
        max_lvl: max_lvl as u32,
        owner: owner_id,
        owner_is_npc,
        npc_event: ev.chars().take(crate::script::npc_run::EVENT_NAME_LENGTH - 1).collect(),
        kick_list: Vec::new(),
        m: pos.0,
        x: pos.1,
        y: pos.2,
    };
    c.db().chat_room().insert(cd.clone());
    Some(cd)
}

/// chat.cpp chat_createpcchat: a player opens a room.
pub fn chat_createpcchat(c: &Ctx, aid: u32, title: &[u8], pass: &[u8], limit: i32, is_pub: bool) -> i32 {
    let Some(p) = pc::get(c, aid) else { return 0 };
    let Some(mut sd) = unit::get(c, aid) else { return 0 };
    if p.chat_id != 0 {
        return 0; // Prevent people abusing the chat system by creating multiple chats, as pointed out by End of Exam. [Skotlex]
    }
    if p.vending || p.buyingstore {
        return 0; // not chat, when you already have a store open
    }
    if crate::npc::mapflag(c, sd.map, MF_NOCHAT) {
        clif::displaymessage(c, &sd, crate::data::msg_txt(281));
        return 0; // Can't create chatrooms on this map.
    }
    // map_getcell(CELL_CHKNOCHAT): no map sets the nochat cell zone in the Loki data (setcell / NOCHAT zones: phase 5)
    unit::stop_walking(c, &mut sd, unit::USW_FIXPOS, 0);
    unit::save(c, &sd);
    match chat_createchat(c, &sd, title, pass, limit, is_pub, 0, "", 0, 1, MAX_LEVEL as i32) {
        Some(mut cd) => {
            cd.users = vec![aid];
            put(c, &cd);
            pc_setchatid(c, aid, cd.id);
            if let Some(mut sd) = unit::get(c, aid) {
                unit::stop_attack(c, &mut sd);
                unit::save(c, &sd);
            }
            if let Some(sd) = unit::get(c, aid) {
                clif_createchat(c, &sd, CREATEROOM_SUCCESS);
            }
            clif_dispchat(c, &cd);
            use crate::db::misc::achievement::{AG_CHATTING_CREATE, AG_CHATTING_DYING};
            let dead = unit::get(c, aid).map(|u| crate::status::sc::status_isdead(c, &u)).unwrap_or(false);
            if dead {
                crate::achievement::achievement_update_objective(c, Some(aid), AG_CHATTING_DYING, &[1]);
            } else {
                crate::achievement::achievement_update_objective(c, Some(aid), AG_CHATTING_CREATE, &[1]);
            }
        }
        None => clif_createchat(c, &sd, CREATEROOM_LIMIT_EXCEEDED),
    }
    0
}

/// chat.cpp chat_joinchat.
pub fn chat_joinchat(c: &Ctx, aid: u32, chatid: u32, pass: &[u8]) -> i32 {
    let Some(p) = pc::get(c, aid) else { return 0 };
    let Some(mut sd) = unit::get(c, aid) else { return 0 };
    let cd = get(c, chatid);
    let full = |cd: &ChatRoom| (if cd.owner_is_npc { cd.users.len() + 1 } else { cd.users.len() }) >= cd.limit as usize;
    let Some(mut cd) = cd.filter(|cd| cd.m == sd.map && !p.vending && !p.buyingstore && p.chat_id == 0 && !full(cd)) else {
        clif_joinchatfail(c, &sd, ENTERROOM_FULL);
        return 0;
    };
    let pass: Vec<u8> = pass.iter().copied().take_while(|&b| b != 0).take(CHATROOM_PASS_SIZE).collect();
    if !cd.is_pub && pass != cd.pass && !pc::has_permission(c, aid, crate::rules::PC_PERM_JOIN_ALL_CHAT) {
        clif_joinchatfail(c, &sd, ENTERROOM_WRONG_PASSWORD);
        return 0;
    }
    let Some(ch) = pc::character_of(c, aid) else { return 0 };
    let lv = ch.base_level as u32;
    if lv < cd.min_lvl || lv > cd.max_lvl {
        if lv < cd.min_lvl {
            clif_joinchatfail(c, &sd, ENTERROOM_TOO_LOW_LEVEL);
        } else {
            clif_joinchatfail(c, &sd, ENTERROOM_TOO_HIGH_LEVEL);
        }
        return 0;
    }
    if ch.zeny < cd.zeny {
        clif_joinchatfail(c, &sd, ENTERROOM_NO_ZENY);
        return 0;
    }
    if !cd.owner_is_npc && cd.kick_list.contains(&ch.id) {
        clif_joinchatfail(c, &sd, ENTERROOM_KICKED);
        return 0;
    }
    unit::stop_walking(c, &mut sd, unit::USW_FIXPOS, 0);
    unit::save(c, &sd);
    cd.users.push(aid);
    put(c, &cd);
    pc_setchatid(c, aid, cd.id);
    let Some(sd) = unit::get(c, aid) else { return 0 };
    // To the person who newly joined the chat
    clif_joinchatok(c, &sd, &cd);
    // Reports to the persons, who already are in the chat
    clif_addchat(c, &cd, &sd);
    clif_dispchat(c, &cd); // Reported number of changes to the people around
    if !cd.owner_is_npc {
        // map_id2sd(cd->owner->id) (the owner may have gone: then nothing)
        let owner = pc::get(c, cd.owner).map(|p| p.account_id);
        crate::achievement::achievement_update_objective(c, owner, crate::db::misc::achievement::AG_CHATTING_COUNT, &[cd.users.len() as i32]);
    }
    chat_triggerevent(c, &cd); // Event
    0
}

/// chat.cpp chat_leavechat: 0 left, 1 room deleted / failed, -1 not in the room.
pub fn chat_leavechat(c: &Ctx, aid: u32, kicked: bool) -> i32 {
    let Some(mut cd) = get(c, chat_id_of(c, aid)) else {
        pc_setchatid(c, aid, 0);
        return 1;
    };
    let Some(leavechar) = cd.users.iter().position(|&u| u == aid) else {
        // Not found in the chatroom?
        pc_setchatid(c, aid, 0);
        return -1;
    };
    if let Some(sd) = unit::get(c, aid) {
        clif_chat_leave(c, &cd, &sd, kicked);
    }
    pc_setchatid(c, aid, 0);
    cd.users.remove(leavechar);
    put(c, &cd);
    if cd.users.is_empty() && !cd.owner_is_npc {
        // Delete empty chatroom
        clif_clearchat(c, &cd);
        c.db().chat_room().id().delete(cd.id);
        // a Warp Portal under the player takes them now (chat.cpp:173-176, skillunit)
        if let Some(sd) = unit::get(c, aid) {
            if let Some(uid) = crate::skillunit::map_find_skill_unit_oncell(c, &sd, sd.x, sd.y, crate::db::skills::AL_WARP, None, 0) {
                if let Some(g) = crate::skillunit::group_of_unit(c, uid) {
                    crate::skillunit::effect::ext_skill_unit_onplace(c, uid, &sd, g.tick);
                }
            }
        }
        return 1;
    }
    if leavechar == 0 && !cd.owner_is_npc {
        // Set and announce new owner
        cd.owner = cd.users[0];
        put(c, &cd);
        if let Some(nu) = unit::get(c, cd.users[0]) {
            clif_chat_role(c, &cd, &nu);
        }
        clif_clearchat(c, &cd);
        // Adjust Chat location after owner has been changed.
        if let Some(nu) = unit::get(c, cd.users[0]) {
            cd.x = nu.x;
            cd.y = nu.y;
            put(c, &cd);
            if !map_addblock_ok(&cd) {
                return 1;
            }
        }
        clif_dispchat(c, &cd);
    } else {
        clif_dispchat(c, &cd); // refresh chatroom
    }
    0
}

/// map_addblock's failure test for the room's new cell (out of the map).
fn map_addblock_ok(cd: &ChatRoom) -> bool {
    match crate::data::maps().get(cd.m) {
        Some(md) => cd.x >= 0 && cd.x < md.xs && cd.y >= 0 && cd.y < md.ys,
        None => false,
    }
}

fn name_of(c: &Ctx, aid: u32) -> Vec<u8> {
    pc::character_of(c, aid).map(|ch| latin1(&ch.name)).unwrap_or_default()
}

/// strncmp(a, b, NAME_LENGTH) == 0 over NUL-terminated names.
fn name_eq(a: &[u8], b: &[u8]) -> bool {
    let a: Vec<u8> = a.iter().copied().take_while(|&x| x != 0).take(crate::party::NAME_LENGTH).collect();
    let b: Vec<u8> = b.iter().copied().take_while(|&x| x != 0).take(crate::party::NAME_LENGTH).collect();
    a == b
}

/// chat.cpp chat_changechatowner.
pub fn chat_changechatowner(c: &Ctx, aid: u32, nextownername: &[u8]) -> i32 {
    let Some(mut cd) = get(c, chat_id_of(c, aid)) else { return 1 };
    if cd.owner_is_npc || cd.owner != aid {
        return 1;
    }
    let Some(i) = (1..cd.users.len()).find(|&i| name_eq(&name_of(c, cd.users[i]), nextownername)) else {
        return -1; // name not found
    };
    // erase temporarily
    clif_clearchat(c, &cd);
    // set new owner
    cd.owner = cd.users[i];
    // swap the old and new owners' positions
    cd.users.swap(0, i);
    put(c, &cd);
    if let Some(u0) = unit::get(c, cd.users[0]) {
        clif_chat_role(c, &cd, &u0);
    }
    if let Some(ui) = unit::get(c, cd.users[i]) {
        clif_chat_role(c, &cd, &ui);
    }
    // set the new chatroom position
    if let Some(o) = owner_unit(c, &cd) {
        cd.x = o.x;
        cd.y = o.y;
        put(c, &cd);
    }
    if !map_addblock_ok(&cd) {
        return 1;
    }
    // and display again
    clif_dispchat(c, &cd);
    0
}

/// chat.cpp chat_changechatstatus.
pub fn chat_changechatstatus(c: &Ctx, aid: u32, title: &[u8], pass: &[u8], limit: i32, is_pub: bool) -> i32 {
    let Some(mut cd) = get(c, chat_id_of(c, aid)) else { return 1 };
    if cd.owner_is_npc || cd.owner != aid {
        return 1;
    }
    cd.title = title.iter().copied().take_while(|&b| b != 0).take(CHATROOM_TITLE_SIZE - 1).collect();
    cd.pass = pass.iter().copied().take_while(|&b| b != 0).take(CHATROOM_PASS_SIZE - 1).collect();
    cd.limit = limit.min(MAX_CHAT_USERS as i32) as u8;
    cd.is_pub = is_pub;
    put(c, &cd);
    clif_changechatstatus(c, &cd);
    clif_dispchat(c, &cd);
    0
}

/// chat.cpp chat_npckickchat (script waitingroomkick).
pub fn chat_npckickchat(c: &Ctx, chat_id: u32, kickusername: &[u8]) -> i32 {
    let Some(cd) = get(c, chat_id) else { return 0 };
    let Some(&u) = cd.users.iter().find(|&&u| name_eq(&name_of(c, u), kickusername)) else { return -1 };
    chat_leavechat(c, u, true);
    0
}

/// chat.cpp chat_kickchat: the owner kicks a member (who may not come back).
pub fn chat_kickchat(c: &Ctx, aid: u32, kickusername: &[u8]) -> i32 {
    let Some(mut cd) = get(c, chat_id_of(c, aid)) else { return -1 };
    if cd.owner_is_npc || cd.owner != aid {
        return -1;
    }
    let Some(&u) = cd.users.iter().find(|&&u| name_eq(&name_of(c, u), kickusername)) else { return -1 };
    if pc::has_permission(c, u, crate::rules::PC_PERM_NO_CHAT_KICK) {
        return 0; // gm kick protection [Valaris]
    }
    let cid = pc::get(c, u).map(|p| p.char_id).unwrap_or(0);
    if !cd.kick_list.contains(&cid) {
        cd.kick_list.push(cid);
    }
    put(c, &cd);
    chat_leavechat(c, u, true);
    0
}

/// chat.cpp chat_createnpcchat (script waitingroom).
#[allow(clippy::too_many_arguments)]
pub fn chat_createnpcchat(c: &Ctx, npc_id: u32, title: &[u8], limit: i32, is_pub: bool, trigger: i32, ev: &str, zeny: i32, min_lvl: i32, max_lvl: i32) -> i32 {
    let exname = c.db().npc().id().find(npc_id).map(|n| n.exname).unwrap_or_default();
    if npc_chat_id(c, npc_id) != 0 {
        log::error!("chat_createnpcchat: npc '{exname}' already has a chatroom, cannot create new one!");
        return 0;
    }
    if zeny as i64 > MAX_ZENY as i64 || max_lvl as i64 > MAX_LEVEL as i64 {
        log::error!("chat_createnpcchat: npc '{exname}' has a required lvl or amount of zeny over the max limit!");
        return 0;
    }
    let cd = match unit::get(c, npc_id) {
        Some(nd) => chat_createchat(c, &nd, title, b"", limit, is_pub, trigger, ev, zeny, min_lvl, max_lvl),
        None => {
            // A floating NPC ("-" script: nd->m == -1, no block on any map) gets its room all the same: chat.cpp
            // chat_createnpcchat / chat_createchat test no map, the room takes an object id (map_get_new_object_id)
            // and nd->chat_id is set. npc/other/pvp.txt's PVPSpectator does this in its OnInit at boot, so every
            // object id handed out later (chat rooms, floor items) depends on it. Nobody can see or join the room
            // (clif_dispchat's area send on map -1 reaches nobody, chat_joinchat needs cd->m == sd->m).
            let Some(n) = c.db().npc().id().find(npc_id) else { return 0 };
            chat_createchat_at(c, n.id, true, (CHAT_NO_MAP, n.x, n.y), title, b"", limit, is_pub, trigger, ev, zeny, min_lvl, max_lvl)
        }
    };
    if let Some(cd) = cd {
        clif_dispchat(c, &cd);
    }
    0
}

/// chat.cpp chat_deletenpcchat (script delwaitingroom).
pub fn chat_deletenpcchat(c: &Ctx, npc_id: u32) -> i32 {
    let Some(cd) = get(c, npc_chat_id(c, npc_id)) else { return 0 };
    chat_npckickall(c, cd.id);
    let Some(cd) = get(c, cd.id) else { return 0 };
    clif_clearchat(c, &cd);
    c.db().chat_room().id().delete(cd.id);
    0
}

use crate::npc::npc as _;

/// The NPCs that own a waiting room (their nd->chat_id is set), for npc_unload's chat_deletenpcchat when the whole
/// world is unloaded (npc.rs build_world).
pub fn npcs_with_chat(c: &Ctx) -> Vec<u32> {
    c.db().chat_room().iter().filter(|cd| cd.owner_is_npc).map(|cd| cd.owner).collect()
}

/// chat.cpp chat_triggerevent.
fn chat_triggerevent(c: &Ctx, cd: &ChatRoom) {
    if cd.users.len() >= cd.trigger as usize && !cd.npc_event.is_empty() {
        crate::script::npc_run::npc_event_do(c, &cd.npc_event);
    }
}

/// chat.cpp chat_enableevent (at most 127 users are needed to trigger the event).
pub fn chat_enableevent(c: &Ctx, chat_id: u32) -> i32 {
    let Some(mut cd) = get(c, chat_id) else { return 0 };
    cd.trigger &= 0x7f;
    put(c, &cd);
    chat_triggerevent(c, &cd);
    0
}

/// chat.cpp chat_disableevent.
pub fn chat_disableevent(c: &Ctx, chat_id: u32) -> i32 {
    let Some(mut cd) = get(c, chat_id) else { return 0 };
    cd.trigger |= 0x80;
    put(c, &cd);
    0
}

/// chat.cpp chat_npckickall.
pub fn chat_npckickall(c: &Ctx, chat_id: u32) -> i32 {
    loop {
        let Some(cd) = get(c, chat_id) else { return 0 };
        let Some(&last) = cd.users.last() else { return 0 };
        chat_leavechat(c, last, false);
        if get(c, chat_id).map(|cd| cd.users.last() == Some(&last)).unwrap_or(false) {
            // could not leave (not in the room any more): avoid looping forever
            let mut cd = get(c, chat_id).unwrap();
            cd.users.pop();
            put(c, &cd);
        }
    }
}

// ---------------------------------------------------------------------------------------------- clif.cpp senders

/// The generated writers leave a `packetSize` field as given: the packet's length goes there.
pub fn with_len(mut v: Vec<u8>) -> Vec<u8> {
    let n = v.len() as u16;
    v[2..4].copy_from_slice(&n.to_le_bytes());
    v
}

/// clif.cpp clif_createchat: ZC_ACK_CREATE_CHATROOM.
pub fn clif_createchat(c: &Ctx, sd: &Unit, flag: u8) {
    let p = PACKET_ZC_ACK_CREATE_CHATROOM { packetType: PACKET_ZC_ACK_CREATE_CHATROOM::ID as i16, flag };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
}

/// clif.cpp clif_chat_status.
fn clif_chat_status(cd: &ChatRoom) -> u8 {
    if !cd.owner_is_npc {
        if cd.is_pub {
            STATUSROOM_PUBLIC
        } else {
            STATUSROOM_PRIVATE
        }
    } else if cd.limit != 0 {
        STATUSROOM_ARENA
    } else {
        STATUSROOM_PK_ZONE
    }
}

/// clif.cpp clif_chat_usercount (an NPC counts itself).
fn clif_chat_usercount(cd: &ChatRoom) -> u16 {
    if cd.owner_is_npc {
        cd.users.len() as u16 + 1
    } else {
        cd.users.len() as u16
    }
}

/// clif.cpp clif_dispchat: ZC_ROOM_NEWENTRY (the title is not NUL-terminated) around the owner, to everyone who is
/// not in this room.
pub fn clif_dispchat(c: &Ctx, cd: &ChatRoom) {
    let Some(owner) = owner_unit(c, cd) else { return };
    let p = PACKET_ZC_ROOM_NEWENTRY {
        packetType: PACKET_ZC_ROOM_NEWENTRY::ID as i16,
        packetSize: 0,
        owner: owner.id as i32,
        id: cd.id as i32,
        limit: cd.limit as u16,
        users: clif_chat_usercount(cd),
        r#type: clif_chat_status(cd),
        title: cd.title.clone(),
    };
    clif::send(c, &with_len(p.to_vec()), &owner, Target::AreaWosc);
}

/// clif.cpp clif_changechatstatus: ZC_CHANGE_CHATROOM to the room (CHAT with the owner as source: an NPC owner's
/// rooms get nothing, as in rAthena).
pub fn clif_changechatstatus(c: &Ctx, cd: &ChatRoom) {
    let Some(owner) = owner_unit(c, cd) else { return };
    let p = PACKET_ZC_CHANGE_CHATROOM {
        packetType: PACKET_ZC_CHANGE_CHATROOM::ID,
        packetSize: 0,
        ownerId: owner.id,
        chatId: cd.id,
        limit: cd.limit as u16,
        users: clif_chat_usercount(cd),
        flag: clif_chat_status(cd),
        title: cd.title.clone(),
    };
    if owner.kind == BL_PC {
        send(c, &with_len(p.to_vec()), ChatSrc::Pc(owner.id), false);
    }
}

/// clif.cpp clif_clearchat: ZC_DESTROY_ROOM around the owner.
pub fn clif_clearchat(c: &Ctx, cd: &ChatRoom) {
    let Some(owner) = owner_unit(c, cd) else { return };
    let p = PACKET_ZC_DESTROY_ROOM { packetType: PACKET_ZC_DESTROY_ROOM::ID as i16, chatId: cd.id };
    clif::send(c, &p.to_vec(), &owner, Target::AreaWosc);
}

/// clif.cpp clif_joinchatfail: ZC_REFUSE_ENTER_ROOM.
pub fn clif_joinchatfail(c: &Ctx, sd: &Unit, result: u8) {
    let p = PACKET_ZC_REFUSE_ENTER_ROOM { packetType: PACKET_ZC_REFUSE_ENTER_ROOM::ID as i16, result };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
}

/// clif.cpp clif_joinchatok: ZC_ENTER_ROOM (an NPC room lists the NPC as owner first).
pub fn clif_joinchatok(c: &Ctx, sd: &Unit, cd: &ChatRoom) {
    let mut members = Vec::new();
    if cd.owner_is_npc {
        let name = c.db().npc().id().find(cd.owner).map(|n| n.name).unwrap_or_default();
        members.push(PACKET_ZC_ENTER_ROOM_sub { flag: 0, name });
        for &u in cd.users.iter() {
            members.push(PACKET_ZC_ENTER_ROOM_sub { flag: 1, name: from_latin1(&name_of(c, u)) });
        }
    } else {
        for (i, &u) in cd.users.iter().enumerate() {
            members.push(PACKET_ZC_ENTER_ROOM_sub { flag: (i > 0) as u32, name: from_latin1(&name_of(c, u)) });
        }
    }
    let p = PACKET_ZC_ENTER_ROOM { packetType: PACKET_ZC_ENTER_ROOM::ID, packetSize: 0, chatId: cd.id, members };
    clif::send(c, &with_len(p.to_vec()), sd, Target::SelfOnly);
}

/// clif.cpp clif_addchat: ZC_MEMBER_NEWENTRY to the others in the room.
pub fn clif_addchat(c: &Ctx, cd: &ChatRoom, sd: &Unit) {
    let p = PACKET_ZC_MEMBER_NEWENTRY { packetType: PACKET_ZC_MEMBER_NEWENTRY::ID as i16, count: cd.users.len() as u16, name: from_latin1(&name_of(c, sd.id)) };
    send(c, &p.to_vec(), ChatSrc::Pc(sd.id), true);
}

/// clif.cpp clif_chat_role: ZC_ROLE_CHANGE to the room.
pub fn clif_chat_role(c: &Ctx, cd: &ChatRoom, sd: &Unit) {
    let p = PACKET_ZC_ROLE_CHANGE { packetType: PACKET_ZC_ROLE_CHANGE::ID as i16, flag: (cd.users.first() != Some(&sd.id)) as i32, name: from_latin1(&name_of(c, sd.id)) };
    send(c, &p.to_vec(), ChatSrc::Pc(sd.id), false);
}

/// clif.cpp clif_chat_leave: ZC_MEMBER_EXIT to the room (still including the one leaving).
pub fn clif_chat_leave(c: &Ctx, cd: &ChatRoom, sd: &Unit, kicked: bool) {
    let p = PACKET_ZC_MEMBER_EXIT { packetType: PACKET_ZC_MEMBER_EXIT::ID as i16, count: (cd.users.len() as u16).wrapping_sub(1), name: from_latin1(&name_of(c, sd.id)), kicked: kicked as u8 };
    send(c, &p.to_vec(), ChatSrc::Pc(sd.id), false);
}

/// clif_parse_GlobalMessage in a room: clif_GlobalMessage(sd, output, CHAT_WOS) — ZC_NOTIFY_CHAT to the others.
pub fn global_message(c: &Ctx, sd: &Unit, output: &str) {
    let mut b = latin1(output);
    b.truncate(crate::net::CHAT_SIZE_MAX - 1);
    b.push(0);
    let p = PACKET_ZC_NOTIFY_CHAT { PacketType: PACKET_ZC_NOTIFY_CHAT::ID as i16, PacketLength: 0, GID: sd.id, Message: b };
    send(c, &p.to_vec(), ChatSrc::Pc(sd.id), true);
}

/// clif.cpp clif_GlobalMessage(…, CHAT_WOS) of a room: ZC_NOTIFY_CHAT from the room itself (script chatmes).
pub fn chatmes(c: &Ctx, chat_id: u32, message: &[u8]) {
    let mut b: Vec<u8> = message.iter().copied().take_while(|&x| x != 0).take(crate::net::CHAT_SIZE_MAX - 1).collect();
    b.push(0);
    let p = PACKET_ZC_NOTIFY_CHAT { PacketType: PACKET_ZC_NOTIFY_CHAT::ID as i16, PacketLength: 0, GID: chat_id, Message: b };
    send(c, &p.to_vec(), ChatSrc::Room(chat_id), true);
}

// ---------------------------------------------------------------------------------------------- view hooks

/// clif_getareachar_pc: a player who owns a room shows it to the area (clif_dispchat, AREA_WOSC as in rAthena).
pub fn getareachar_pc(c: &Ctx, dstsd: &Unit) {
    if let Some(cd) = get(c, chat_id_of(c, dstsd.id)) {
        if cd.users.first() == Some(&dstsd.id) {
            clif_dispchat(c, &cd);
        }
    }
}

/// clif_getareachar_unit's NPC part: the NPC's waiting room.
pub fn getareachar_npc(c: &Ctx, nd: &Unit) {
    if let Some(cd) = get(c, npc_chat_id(c, nd.id)) {
        clif_dispchat(c, &cd);
    }
}

/// clif_outsight: a room owner going out of someone's sight re-displays the room (clif_dispchat).
pub fn outsight_pc(c: &Ctx, sd: &Unit) {
    if let Some(cd) = get(c, chat_id_of(c, sd.id)) {
        if cd.users.first() == Some(&sd.id) {
            clif_dispchat(c, &cd);
        }
    }
}

// ---------------------------------------------------------------------------------------------- clif.cpp parsers

/// npc.cpp npc_isnear: a visible, non-dynamic NPC within min_npc_vendchat_distance.
pub(crate) fn npc_isnear(c: &Ctx, sd: &Unit) -> bool {
    if min_npc_vendchat_distance() <= 0 {
        return false;
    }
    crate::map::units_in_range(c, sd.map, sd.x, sd.y, min_npc_vendchat_distance(), BL_NPC)
        .iter()
        .any(|nd| nd.sc.option & crate::unit::OPTION_HIDE == 0 && nd.npc_owner_char == 0)
}

/// clif.hpp USESKILL_FAIL_THERE_ARE_NPC_AROUND.
const USESKILL_FAIL_THERE_ARE_NPC_AROUND: u8 = 83;

/// clif.cpp clif_parse_CreateChatRoom (CZ_CREATE_CHATROOM 0x00d5).
pub fn parse_create_chat_room(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(sd) = unit::get(c, aid) else { return };
    let Some(p) = PACKET_CZ_CREATE_CHATROOM::read(&mut Reader::new(pkt)) else { return };
    const MANNER_NOROOM: i32 = 0x10; // status.hpp manner_flags
    if crate::status::sc::get_sce(c, aid, crate::status::consts::SC_NOCHAT).map(|e| e.val1 & MANNER_NOROOM != 0).unwrap_or(false) {
        return;
    }
    if crate::conf::basic_skill_check() && crate::skill::checkskill(c, aid, 1) < 4 && crate::skill::checkskill(c, aid, 5018) < 1 {
        crate::skill::skill_fail(c, &sd, 1, 3);
        return;
    }
    if npc_isnear(c, &sd) {
        crate::skill::skill_fail_ex(c, &sd, 1, USESKILL_FAIL_THERE_ARE_NPC_AROUND, 0, 0);
        return;
    }
    if (p.packetSize as usize) < PACKET_CZ_CREATE_CHATROOM::SIZE {
        return; // invalid input
    }
    let password: Vec<u8> = latin1(&p.password).into_iter().take(CHATROOM_PASS_SIZE - 1).collect();
    let tlen = (CHATROOM_TITLE_SIZE).min(p.packetSize as usize - PACKET_CZ_CREATE_CHATROOM::SIZE + 1);
    let title: Vec<u8> = p.title.iter().copied().take_while(|&b| b != 0).take(tlen.saturating_sub(1)).collect();
    chat_createpcchat(c, aid, &title, &password, p.limit as i32, p.r#type != 0);
}

/// clif.cpp clif_parse_ChatAddMember (CZ_REQ_ENTER_ROOM 0x00d9).
pub fn parse_chat_add_member(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_REQ_ENTER_ROOM::read(&mut Reader::new(pkt)) else { return };
    chat_joinchat(c, aid, p.chat_id, &latin1(&p.password));
}

/// clif.cpp clif_parse_ChatRoomStatusChange (CZ_CHANGE_CHATROOM 0x00de).
pub fn parse_chat_room_status_change(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_CHANGE_CHATROOM::read(&mut Reader::new(pkt)) else { return };
    let len = (p.packetSize as usize).saturating_sub(PACKET_CZ_CHANGE_CHATROOM::SIZE);
    if len == 0 {
        return; // invalid input
    }
    let password: Vec<u8> = latin1(&p.password).into_iter().take(CHATROOM_PASS_SIZE - 1).collect();
    let tlen = (len + 1).min(CHATROOM_TITLE_SIZE);
    let title: Vec<u8> = p.title.iter().copied().take_while(|&b| b != 0).take(tlen - 1).collect();
    chat_changechatstatus(c, aid, &title, &password, p.limit as i32, p.r#type != 0);
}

fn pos_bytes(pkt: &[u8], i: usize, n: usize) -> Vec<u8> {
    let cmd = u16::from_le_bytes([pkt[0], pkt[1]]);
    let off = packet_pos(cmd).get(i).copied().unwrap_or(2) as usize;
    pkt.get(off..).unwrap_or(&[]).iter().copied().take(n).take_while(|&b| b != 0).collect()
}

/// clif.cpp clif_parse_ChangeChatOwner (CZ_REQ_ROLE_CHANGE 0x00e0: role, name).
pub fn parse_change_chat_owner(c: &Ctx, aid: u32, pkt: &[u8]) {
    chat_changechatowner(c, aid, &pos_bytes(pkt, 1, crate::party::NAME_LENGTH));
}

/// clif.cpp clif_parse_KickFromChat (CZ_REQ_EXPEL_MEMBER 0x00e2).
pub fn parse_kick_from_chat(c: &Ctx, aid: u32, pkt: &[u8]) {
    chat_kickchat(c, aid, &pos_bytes(pkt, 0, crate::party::NAME_LENGTH));
}

/// clif.cpp clif_parse_ChatLeave (CZ_EXIT_ROOM 0x00e3).
pub fn parse_chat_leave(c: &Ctx, aid: u32) {
    chat_leavechat(c, aid, false);
}
