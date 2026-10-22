//! The player's social state (map_session_data's party / friend / ignore / channel fields, `pc_social`), the friend
//! list (mmo_charstatus friends[], clif.cpp clif_friendslist_* and clif_parse_FriendsList*), whisper ignore lists
//! (clif_parse_PMIgnore*, clif_wisexin / clif_wisall) and @noask's auto-reject notice (clif_noask_sub).

use spacetimedb::{table, SpacetimeType, Table};

use crate::character::character;
use crate::clif::{self, Target};
use crate::net::packets::*;
use crate::net::wire::{from_latin1, latin1, Reader};
use crate::pc;
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// mmo.hpp MAX_FRIENDS.
pub const MAX_FRIENDS: usize = 40;
/// map.hpp MAX_IGNORE_LIST.
pub const MAX_IGNORE_LIST: usize = 20;
/// player.conf `friend_auto_add: yes`.
pub fn friend_auto_add() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::FRIEND_AUTO_ADD) != 0
}

/// map_session_data fields of the social systems, for an online character (dropped at unit_free).
#[table(accessor = pc_social)]
#[derive(Clone, Debug, Default)]
pub struct PcSocial {
    #[primary_key]
    pub account_id: u32,
    // party.cpp
    pub party_invite: u32,
    pub party_invite_account: u32,
    pub party_creating: bool,
    pub party_joining: bool,
    /// sd->party_booking_requests: char ids that asked to join through the recruitment board.
    pub party_booking_requests: Vec<u32>,
    /// sd->state.noask (@noask).
    pub noask: bool,
    /// sd->friend_req.
    pub friend_req: u32,
    /// sd->ignore[] names (in order) and sd->state.ignoreAll.
    pub ignore: Vec<String>,
    pub ignore_all: bool,
    /// sd->channels[] (channel keys, channel.rs) with their sd->channel_tick[].
    pub channels: Vec<String>,
    pub channel_tick: Vec<i64>,
    /// sd->gcbind (channel key or "").
    pub gcbind: String,
    /// sd->stealth (a channel admin joined a password channel without the password).
    pub stealth: bool,
    /// sd->fontcolor (@fontcolor index into the channel colours; 0 = normal).
    pub fontcolor: u8,
}

/// The social state of `aid` (a fresh one when it has none yet).
pub fn sx(c: &Ctx, aid: u32) -> PcSocial {
    c.db().pc_social().account_id().find(aid).unwrap_or(PcSocial { account_id: aid, ..Default::default() })
}

pub fn put_sx(c: &Ctx, x: &PcSocial) {
    if c.db().pc_social().account_id().find(x.account_id).is_some() {
        c.db().pc_social().account_id().update(x.clone());
    } else {
        c.db().pc_social().insert(x.clone());
    }
}

/// unit_free: the session's social state goes with it.
pub fn drop_sx(c: &Ctx, aid: u32) {
    c.db().pc_social().account_id().delete(aid);
}

// ---------------------------------------------------------------------------------------------- friends

/// mmo.hpp struct s_friend.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct Friend {
    pub account_id: u32,
    pub char_id: u32,
    pub name: String,
}

/// mmo_charstatus friends[] of a character (rAthena's `friends` table holds the char ids; char.cpp saves the array
/// and loads it back with the friends' current names).
#[table(accessor = char_friends)]
#[derive(Clone, Debug)]
pub struct CharFriends {
    #[primary_key]
    pub char_id: u32,
    pub friends: Vec<Friend>,
}

pub fn friends_of(c: &Ctx, char_id: u32) -> Vec<Friend> {
    c.db().char_friends().char_id().find(char_id).map(|r| r.friends).unwrap_or_default()
}

fn put_friends(c: &Ctx, char_id: u32, friends: Vec<Friend>) {
    if c.db().char_friends().char_id().find(char_id).is_some() {
        c.db().char_friends().char_id().update(CharFriends { char_id, friends });
    } else {
        c.db().char_friends().insert(CharFriends { char_id, friends });
    }
}

/// char.cpp char_mmo_char_fromsql's friend list (on login): `SELECT c.account_id, c.char_id, c.name FROM char c
/// LEFT JOIN friends f ON f.friend_id = c.char_id WHERE f.char_id = ? LIMIT MAX_FRIENDS` — friends that still exist,
/// with their current names, in friend char id order (the friends table's primary key).
pub fn friends_load(c: &Ctx, char_id: u32) {
    let mut list: Vec<Friend> = Vec::new();
    let mut ids: Vec<u32> = friends_of(c, char_id).into_iter().map(|f| f.char_id).collect();
    ids.sort_unstable();
    ids.dedup();
    for fid in ids {
        if list.len() >= MAX_FRIENDS {
            break;
        }
        if let Some(ch) = c.db().character().id().find(fid) {
            list.push(Friend { account_id: ch.account_id, char_id: ch.id, name: ch.name.clone() });
        }
    }
    put_friends(c, char_id, list);
}

/// char_mapif.cpp chmapif_parse_askrmfriend (0x2b07, sent by chrif.cpp chrif_removefriend): the friend was offline;
/// its row goes (`DELETE ... LIMIT 1`: a friend is listed once).
pub fn char_removefriend(c: &Ctx, char_id: u32, friend_id: u32) {
    let mut list = friends_of(c, char_id);
    list.retain(|f| f.char_id != friend_id);
    put_friends(c, char_id, list);
}

/// clif.cpp clif_friendslist_toggle: ZC_FRIENDS_STATE.
pub fn clif_friendslist_toggle(c: &Ctx, sd: &Unit, f: &Friend, online: bool) {
    let p = PACKET_ZC_FRIENDS_STATE { packetType: PACKET_ZC_FRIENDS_STATE::ID as i16, AID: f.account_id, CID: f.char_id, offline: (!online) as u8, name: f.name.clone() };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
}

/// clif.cpp clif_friendslist_toggle_sub over one player: tell `tsd` that (account_id, char_id) came on / went off.
fn clif_friendslist_toggle_sub(c: &Ctx, tsd: &Unit, account_id: u32, char_id: u32, online: bool) -> bool {
    for f in friends_of(c, tsd.char_id) {
        if f.char_id == 0 {
            break;
        }
        if f.account_id != account_id || f.char_id != char_id {
            continue;
        }
        clif_friendslist_toggle(c, tsd, &f, online);
        return true;
    }
    false
}

/// clif.cpp clif_friendslist_send: ZC_FRIENDS_LIST, then the online state of each online friend.
pub fn friendslist_send(c: &Ctx, sd: &Unit) {
    let list = friends_of(c, sd.char_id);
    let friends = list.iter().take(MAX_FRIENDS).take_while(|f| f.char_id != 0).map(|f| PACKET_ZC_FRIENDS_LIST_sub { AID: f.account_id, CID: f.char_id, name: f.name.clone() }).collect();
    let p = PACKET_ZC_FRIENDS_LIST { packetType: PACKET_ZC_FRIENDS_LIST::ID as i16, PacketLength: 0, friends };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
    // Sending the online players
    for f in list {
        if f.char_id == 0 {
            break;
        }
        if pc::charid2sd(c, f.char_id).is_some() {
            clif_friendslist_toggle(c, sd, &f, true);
        }
    }
}

/// Every online player in pc_db iteration order (map_foreachpc / mapit_getallusers).
pub fn all_pc(c: &Ctx) -> Vec<Unit> {
    use crate::pc::pc as _;
    let mut v: Vec<u32> = c.db().pc().iter().map(|p| p.account_id).collect();
    v.sort_by_key(|&a| crate::party::idb_order(a));
    v.into_iter().filter_map(|a| unit::get(c, a)).collect()
}

/// clif_parse_LoadEndAck (connect_new) / unit_free: tell this character's friends it came online / went offline.
fn friends_toggle(c: &Ctx, sd: &Unit, online: bool) {
    if friend_auto_add() {
        for my_friend in friends_of(c, sd.char_id) {
            // Cancel early
            if my_friend.char_id == 0 {
                break;
            }
            if let Some(tsd) = pc::charid2sd(c, my_friend.char_id) {
                for their_friend in friends_of(c, tsd.char_id) {
                    // Cancel early
                    if their_friend.char_id == 0 {
                        break;
                    }
                    if their_friend.account_id != sd.id || their_friend.char_id != sd.char_id {
                        continue;
                    }
                    clif_friendslist_toggle(c, &tsd, &their_friend, online);
                    break;
                }
            }
        }
    } else {
        for tsd in all_pc(c) {
            clif_friendslist_toggle_sub(c, &tsd, sd.id, sd.char_id, online);
        }
    }
}

/// clif_parse_LoadEndAck (connect_new): "Notify everyone that this char logged in."
pub fn friends_toggle_login(c: &Ctx, sd: &Unit, online: bool) {
    friends_toggle(c, sd, online);
}

/// clif.cpp clif_friendslist_reqack: ZC_ADD_FRIENDS_LIST (0x0209).
fn clif_friendslist_reqack(c: &Ctx, sd: &Unit, f_sd: &Unit, kind: u16) {
    let Some(fch) = pc::character_of(c, f_sd.id) else { return };
    let mut w = Vec::with_capacity(36);
    w.extend_from_slice(&0x0209u16.to_le_bytes());
    w.extend_from_slice(&kind.to_le_bytes());
    w.extend_from_slice(&fch.account_id.to_le_bytes());
    w.extend_from_slice(&fch.id.to_le_bytes());
    crate::net::wire::put_str(&mut w, &fch.name, crate::party::NAME_LENGTH);
    clif::send(c, &w, sd, Target::SelfOnly);
}

/// clif.cpp clif_friendlist_req: ZC_REQ_ADD_FRIENDS (0x0207).
fn clif_friendlist_req(c: &Ctx, sd: &Unit, account_id: u32, char_id: u32, name: &str) {
    let mut w = Vec::with_capacity(34);
    w.extend_from_slice(&0x0207u16.to_le_bytes());
    w.extend_from_slice(&account_id.to_le_bytes());
    w.extend_from_slice(&char_id.to_le_bytes());
    crate::net::wire::put_str(&mut w, name, crate::party::NAME_LENGTH);
    clif::send(c, &w, sd, Target::SelfOnly);
}

/// ZC_DELETE_FRIENDS (0x020a).
fn clif_friend_deleted(c: &Ctx, sd: &Unit, account_id: u32, char_id: u32) {
    let mut w = Vec::with_capacity(10);
    w.extend_from_slice(&0x020au16.to_le_bytes());
    w.extend_from_slice(&account_id.to_le_bytes());
    w.extend_from_slice(&char_id.to_le_bytes());
    clif::send(c, &w, sd, Target::SelfOnly);
}

fn pos_reader<'a>(pkt: &'a [u8], i: usize) -> Reader<'a> {
    let cmd = u16::from_le_bytes([pkt[0], pkt[1]]);
    let pos = packet_pos(cmd);
    Reader::at(pkt, pos.get(i).copied().unwrap_or(2) as usize)
}

/// clif.cpp clif_parse_FriendsListAdd (CZ_ADD_FRIENDS 0x0202).
pub fn parse_friends_list_add(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(sd) = unit::get(c, aid) else { return };
    let name = pos_reader(pkt, 0).string(crate::party::NAME_LENGTH).unwrap_or_default();
    // Friend doesn't exist (no player with this name)
    let Some(f_sd) = pc::nick2sd(c, &name, false) else {
        clif::displaymessage(c, &sd, crate::data::msg_txt(3));
        return;
    };
    if sd.id == f_sd.id {
        return; // adding oneself as friend
    }
    let mine = friends_of(c, sd.char_id);
    // ensure that the request player's friend list is not full
    if mine.len() >= MAX_FRIENDS {
        clif_friendslist_reqack(c, &sd, &f_sd, 2);
        return;
    }
    let mut fx = sx(c, f_sd.id);
    // @noask [LuzZza]
    if fx.noask {
        clif_noask_sub(c, &sd, &f_sd, 398); // Autorejected friend request from %s.
        return;
    }
    // Friend already exists
    if mine.iter().any(|f| f.char_id == f_sd.char_id) {
        clif::displaymessage(c, &sd, crate::data::msg_txt(671)); // "Friend already exists."
        return;
    }
    fx.friend_req = sd.char_id;
    put_sx(c, &fx);
    let mut x = sx(c, aid);
    x.friend_req = f_sd.char_id;
    put_sx(c, &x);
    let name = pc::character_of(c, aid).map(|ch| ch.name).unwrap_or_default();
    clif_friendlist_req(c, &f_sd, sd.id, sd.char_id, &name);
}

/// clif.cpp clif_parse_FriendsListReply (CZ_ACK_REQ_ADD_FRIENDS 0x0208: inviter aid, cid, reply.L).
pub fn parse_friends_list_reply(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(sd) = unit::get(c, aid) else { return };
    let account_id = pos_reader(pkt, 0).u32().unwrap_or(0);
    let reply = pos_reader(pkt, 2).i32().unwrap_or(0) as i8; // `char reply = RFIFOL(…)`
    if sd.id == account_id {
        return; // adding oneself as friend
    }
    // The account id is the same as the bl.id of players.
    let Some(f_sd) = unit::get(c, account_id).filter(|u| pc::get(c, u.id).is_some()) else { return };
    let x = sx(c, aid);
    let fx = sx(c, f_sd.id);
    if reply == 0 || !(x.friend_req == f_sd.char_id && fx.friend_req == sd.char_id) {
        clif_friendslist_reqack(c, &f_sd, &sd, 1);
        return;
    }
    // Find an empty slot
    let mut theirs = friends_of(c, f_sd.char_id);
    if theirs.len() >= MAX_FRIENDS {
        clif_friendslist_reqack(c, &f_sd, &sd, 2);
        return;
    }
    let my_name = pc::character_of(c, aid).map(|ch| ch.name).unwrap_or_default();
    theirs.push(Friend { account_id: sd.id, char_id: sd.char_id, name: my_name });
    let slot = theirs.len() as i32; // i + 1: the friend went into the first empty slot i
    put_friends(c, f_sd.char_id, theirs);
    clif_friendslist_reqack(c, &f_sd, &sd, 0);
    crate::achievement::achievement_update_objective(c, Some(f_sd.id), crate::db::misc::achievement::AG_ADD_FRIEND, &[slot]);
    if friend_auto_add() {
        // Also add f_sd to sd's friendlist.
        let mut mine = friends_of(c, sd.char_id);
        if mine.iter().any(|f| f.char_id == f_sd.char_id) {
            return; // No need to add anything.
        }
        if mine.len() >= MAX_FRIENDS {
            clif_friendslist_reqack(c, &sd, &f_sd, 2);
            return;
        }
        let f_name = pc::character_of(c, f_sd.id).map(|ch| ch.name).unwrap_or_default();
        mine.push(Friend { account_id: f_sd.id, char_id: f_sd.char_id, name: f_name });
        let slot = mine.len() as i32;
        put_friends(c, sd.char_id, mine);
        clif_friendslist_reqack(c, &sd, &f_sd, 0);
        crate::achievement::achievement_update_objective(c, Some(sd.id), crate::db::misc::achievement::AG_ADD_FRIEND, &[slot]);
    }
}

/// clif.cpp clif_parse_FriendsListRemove (CZ_DELETE_FRIENDS 0x0203: aid, cid).
pub fn parse_friends_list_remove(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(sd) = unit::get(c, aid) else { return };
    let account_id = pos_reader(pkt, 0).u32().unwrap_or(0);
    let char_id = pos_reader(pkt, 1).u32().unwrap_or(0);
    // Search friend
    let mine = friends_of(c, sd.char_id);
    if !mine.iter().any(|f| f.char_id == char_id && f.account_id == account_id) {
        clif::displaymessage(c, &sd, crate::data::msg_txt(672)); // "Name not found in list."
        return;
    }
    // remove from friend's list first
    match unit::get(c, account_id).filter(|u| pc::get(c, u.id).is_some() && u.char_id == char_id) {
        Some(f_sd) => {
            let mut theirs = friends_of(c, f_sd.char_id);
            if let Some(i) = theirs.iter().position(|f| f.char_id == sd.char_id && f.account_id == sd.id) {
                // move all chars up
                theirs.remove(i);
                put_friends(c, f_sd.char_id, theirs);
                // should the guy be notified of some message? we should add it here if so
                clif_friend_deleted(c, &f_sd, sd.id, sd.char_id);
            }
        }
        None => {
            // friend not online -- ask char server to delete from his friendlist
            crate::intif::send(crate::intif::Inter::RemoveFriend { char_id, friend_id: sd.char_id });
        }
    }
    // We can now delete from original requester
    let mut mine = friends_of(c, sd.char_id);
    if let Some(i) = mine.iter().position(|f| f.char_id == char_id && f.account_id == account_id) {
        mine.remove(i);
    }
    put_friends(c, sd.char_id, mine);
    clif::displaymessage(c, &sd, crate::data::msg_txt(674)); // "Friend removed"
    clif_friend_deleted(c, &sd, account_id, char_id);
}

/// Whether `friend_char_id` is in the character's friend list. A module helper, not an rAthena function (rAthena has
/// no pc_isfriend: code that needs it scans sd->status.friends inline); unused for now.
pub fn pc_isfriend(c: &Ctx, char_id: u32, friend_char_id: u32) -> bool {
    friends_of(c, char_id).iter().any(|f| f.char_id == friend_char_id)
}

// ---------------------------------------------------------------------------------------------- @noask

/// clif.hpp color_table (RGB → BGR at do_init_clif).
pub const COLOR_DEFAULT: u32 = 0x00FF00;
pub const COLOR_RED: u32 = 0x0000FF;
pub const COLOR_LIGHT_GREEN: u32 = 0xB5FFB5;

/// clif.cpp clif_noask_sub: tells both sides that a request was auto-rejected by @noask.
pub fn clif_noask_sub(c: &Ctx, sd: &Unit, tsd: &Unit, kind: u16) {
    // Your request has been rejected by autoreject option.
    clif::messagecolor_target(c, sd, COLOR_LIGHT_GREEN, &latin1(crate::data::msg_txt(392)), false, Target::SelfOnly, None);
    // Notice that a request was rejected.
    let name = pc::character_of(c, sd.id).map(|ch| ch.name).unwrap_or_default();
    let mut out = latin1(&crate::data::format_msg(crate::data::msg_txt(kind), &[&name]));
    out.truncate(crate::net::CHAT_SIZE_MAX - 1);
    clif::messagecolor_target(c, tsd, COLOR_LIGHT_GREEN, &out, false, Target::SelfOnly, None);
}

// ---------------------------------------------------------------------------------------------- whisper ignore

/// clif.cpp clif_wisexin: ZC_SETTING_WHISPER_PC.
fn clif_wisexin(c: &Ctx, sd: &Unit, kind: u8, flag: u8) {
    let p = PACKET_ZC_SETTING_WHISPER_PC { packetType: PACKET_ZC_SETTING_WHISPER_PC::ID as i16, r#type: kind, result: flag };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
}

/// clif.cpp clif_wisall: ZC_SETTING_WHISPER_STATE.
fn clif_wisall(c: &Ctx, sd: &Unit, kind: u8, failure: bool) {
    let p = PACKET_ZC_SETTING_WHISPER_STATE { packetType: PACKET_ZC_SETTING_WHISPER_STATE::ID as i16, r#type: kind, result: failure as u8 };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
}

/// clif.cpp clif_parse_PMIgnore (CZ_SETTING_WHISPER_PC 0x00cf: /ex nick, /in nick).
pub fn parse_pm_ignore(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(sd) = unit::get(c, aid) else { return };
    // nick[NAME_LENGTH-1] = '\0': at most 23 characters
    let raw = pos_reader(pkt, 0).string(crate::party::NAME_LENGTH).unwrap_or_default();
    let nick = from_latin1(&latin1(&raw).into_iter().take(crate::party::NAME_LENGTH - 1).collect::<Vec<u8>>());
    let kind = pos_reader(pkt, 1).u8().unwrap_or(0);
    let mut x = sx(c, aid);
    if kind == 0 {
        // Add name to ignore list (block)
        if crate::rules::WISP_SERVER_NAME == nick {
            clif_wisexin(c, &sd, kind, 1); // fail
            return;
        }
        // try to find a free spot, while checking for duplicates at the same time (an empty nick matches the first
        // free slot and is "inserted" as the empty string it already holds: success, nothing stored)
        if nick.is_empty() && x.ignore.len() < MAX_IGNORE_LIST {
            clif_wisexin(c, &sd, kind, 0);
            return;
        }
        if x.ignore.iter().any(|n| *n == nick) {
            clif_wisexin(c, &sd, kind, 0); // Aegis reports success.
            return;
        }
        if x.ignore.len() >= MAX_IGNORE_LIST {
            // no space for new entry
            clif_wisexin(c, &sd, kind, 2); // too many blocks
            return;
        }
        // Insert in position i
        x.ignore.push(nick);
    } else {
        // Remove name from ignore list (unblock)
        let Some(i) = x.ignore.iter().position(|n| *n == nick) else {
            clif_wisexin(c, &sd, kind, 1); // fail
            return;
        };
        // move everything one place down to overwrite removed entry
        x.ignore.remove(i);
    }
    put_sx(c, &x);
    clif_wisexin(c, &sd, kind, 0); // success
}

/// clif.cpp clif_parse_PMIgnoreAll (CZ_SETTING_WHISPER_STATE 0x00d0: /exall, /inall).
pub fn parse_pm_ignore_all(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(sd) = unit::get(c, aid) else { return };
    let Some(p) = PACKET_CZ_SETTING_WHISPER_STATE::read(&mut Reader::new(pkt)) else { return };
    let mut x = sx(c, aid);
    let failure;
    if p.r#type == 0 {
        // Deny all
        if x.ignore_all {
            failure = true;
        } else {
            x.ignore_all = true;
            failure = false;
        }
    } else if x.ignore_all {
        // Unblock everyone
        x.ignore_all = false;
        failure = false;
    } else if !x.ignore.is_empty() {
        // Wipe the ignore list.
        x.ignore.clear();
        failure = false;
    } else {
        failure = true;
    }
    put_sx(c, &x);
    clif_wisall(c, &sd, p.r#type as u8, failure);
}

/// clif.cpp clif_PMIgnoreList / clif_parse_PMIgnoreList (CZ_REQ_WHISPER_LIST 0x00d3): ZC_WHISPER_LIST.
pub fn parse_pm_ignore_list(c: &Ctx, aid: u32) {
    let Some(sd) = unit::get(c, aid) else { return };
    let x = sx(c, aid);
    let names = x.ignore.iter().take(MAX_IGNORE_LIST).map(|n| PACKET_ZC_WHISPER_LIST_sub { name: n.clone() }).collect();
    let p = PACKET_ZC_WHISPER_LIST { packetType: PACKET_ZC_WHISPER_LIST::ID as i16, packetSize: 0, names };
    clif::send(c, &crate::chat::with_len(p.to_vec()), &sd, Target::SelfOnly);
}

/// [`whisper_refused`]'s answer for an autotrader (not an ACKWHISPER result: clif_parse_WisMessage whispers
/// "%s is in autotrade mode and cannot receive whispered messages." from wisp_server_name instead).
pub const WHISPER_AUTOTRADE: u8 = 0xff;

/// clif_parse_WisMessage's checks on the receiver: Some(result) when the whisper is refused
/// (ACKWHISPER_TARGET_OFFLINE 1 / IGNORED 2 / ALL_IGNORED 3, or [`WHISPER_AUTOTRADE`]).
pub fn whisper_refused(c: &Ctx, sd: &Unit, dstsd: &Unit) -> Option<u8> {
    let x = sx(c, dstsd.id);
    let lv = pc::group_level(c, sd.id);
    let dlv = pc::group_level(c, dstsd.id);
    // if player ignores everyone
    if x.ignore_all && lv <= dlv {
        let invisible = dstsd.sc.option & crate::unit::OPTION_INVISIBLE != 0;
        return Some(if invisible && lv < dlv { 1 } else { 3 });
    }
    // if player is autotrading (`dstsd->state.autotrade == 1`): the caller answers with a whisper from the server
    if crate::autotrade::autotrade(c, dstsd.id) == 1 {
        return Some(WHISPER_AUTOTRADE);
    }
    if lv <= dlv {
        // if player ignores the source character
        let name = pc::character_of(c, sd.id).map(|ch| ch.name).unwrap_or_default();
        if x.ignore.iter().any(|n| *n == name) {
            return Some(2);
        }
    }
    None
}
