//! map/party.cpp: parties on the map server — its party_db (`party_data`, a copy of the char server's party kept up to
//! date from int_party.rs's answers), invitations, leaving / expelling, options, leader changes, member positions and
//! HP bars (party_send_xy_timer), party chat, party booking, and the hooks other systems use (exp sharing, loot
//! sharing, party lookups). The party packets of clif.cpp (clif_party_*, clif_parse_*Party*) are here too.
//!
//! map_session_data's party fields (party_invite, party_creating, …) are the player's `pc_social` row (social.rs);
//! sd->status.party_id is the character's `party_id`.

use spacetimedb::{table, SpacetimeType, Table};

use crate::character::character;
use crate::clif::{self, Target};
use crate::int_party::Party;
use crate::intif::{self, Inter};
use crate::net::packets::*;
use crate::net::wire::{from_latin1, latin1, Reader};
use crate::pc;
use crate::social::{self, PcSocial};
use crate::timer::{self, Func};
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// mmo.hpp MAX_PARTY.
pub const MAX_PARTY: usize = 12;
/// mmo.hpp NAME_LENGTH (23 + NUL).
pub const NAME_LENGTH: usize = 24;
/// party.hpp MAX_PARTY_BOOKING_JOBS / MAX_PARTY_BOOKING_RESULTS.
pub const MAX_PARTY_BOOKING_JOBS: usize = 6;
pub const MAX_PARTY_BOOKING_RESULTS: usize = 10;

/// mmo.hpp e_party_member_withdraw.
pub const PARTY_MEMBER_WITHDRAW_LEAVE: u8 = 0;
pub const PARTY_MEMBER_WITHDRAW_EXPEL: u8 = 1;
pub const PARTY_MEMBER_WITHDRAW_CANT_LEAVE: u8 = 2;
pub const PARTY_MEMBER_WITHDRAW_CANT_EXPEL: u8 = 3;

/// clif.hpp e_party_invite_reply.
pub const PARTY_REPLY_JOIN_OTHER_PARTY: i32 = 0;
pub const PARTY_REPLY_REJECTED: i32 = 1;
pub const PARTY_REPLY_ACCEPTED: i32 = 2;
pub const PARTY_REPLY_FULL: i32 = 3;
pub const PARTY_REPLY_DUAL: i32 = 4;
pub const PARTY_REPLY_JOINMSG_REFUSE: i32 = 5;
pub const PARTY_REPLY_OFFLINE: i32 = 7;
pub const PARTY_REPLY_MEMORIALDUNGEON: i32 = 10;

/// conf/battle/party.conf and player.conf / instance.conf values the party code reads.
/// party.conf `party_update_interval: 1000`.
pub fn party_update_interval() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::PARTY_UPDATE_INTERVAL) as i64
}
/// party.conf `party_hp_mode: 0` (HP updates are sent as they happen, not by the timer).
pub fn party_hp_mode() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::PARTY_HP_MODE)
}
/// party.conf `party_item_share_type: 0`.
pub fn party_share_type() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::PARTY_ITEM_SHARE_TYPE)
}
/// party.conf `show_party_share_picker: yes`, `show_picker.item_type: 112`.
pub fn party_show_share_picker() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::SHOW_PARTY_SHARE_PICKER) != 0
}
pub fn show_picker_item_type() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::SHOW_PICKER_ITEM_TYPE)
}
/// party.conf `idle_no_share: no`.
pub fn idle_no_share() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::IDLE_NO_SHARE) != 0
}
/// party.conf `party_even_share_bonus: 0`.
pub fn party_even_share_bonus() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::PARTY_EVEN_SHARE_BONUS) as i64
}
/// party.conf `block_account_in_same_party: yes`.
pub fn block_account_in_same_party() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::BLOCK_ACCOUNT_IN_SAME_PARTY) != 0
}
/// party.conf `change_party_leader_samemap: yes`.
pub fn change_party_leader_samemap() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::CHANGE_PARTY_LEADER_SAMEMAP) != 0
}
/// player.conf `invite_request_check: yes`.
pub fn invite_request_check() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::INVITE_REQUEST_CHECK) != 0
}
/// instance.conf `instance_block_leave / leaderchange / invite / expulsion: yes`.
pub fn instance_block_leave() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::INSTANCE_BLOCK_LEAVE) != 0
}
pub fn instance_block_leaderchange() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::INSTANCE_BLOCK_LEADERCHANGE) != 0
}
pub fn instance_block_invite() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::INSTANCE_BLOCK_INVITE) != 0
}
pub fn instance_block_expulsion() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::INSTANCE_BLOCK_EXPULSION) != 0
}

/// common/mmo.hpp struct party_member.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct PartyMember {
    pub account_id: u32,
    pub char_id: u32,
    pub name: String,
    pub class: u16,
    /// Map name without extension (MAP_NAME_LENGTH_EXT buffer).
    pub map: String,
    pub lv: u16,
    pub leader: bool,
    pub online: bool,
}

/// party.hpp struct party_member_data: the online member (account id of its map_session_data, 0 = none) and what
/// party_send_xy_timer last sent for it.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct PartyMemberData {
    pub sd: u32,
    pub hp: u32,
    pub x: u16,
    pub y: u16,
}

/// party.hpp struct party_data: the map server's party_db.
#[table(accessor = party_data)]
#[derive(Clone, Debug)]
pub struct PartyData {
    #[primary_key]
    pub party_id: u32,
    pub party: Party,
    /// Always MAX_PARTY entries.
    pub data: Vec<PartyMemberData>,
    /// For item distribution, position of last picker in party.
    pub itemc: u8,
    pub instance_id: i32,
    /// state bits: 1 monk, 2 star gladiator, 4 super novice, 8 taekwon.
    pub state: u8,
}

pub const PSTATE_MONK: u8 = 1;
pub const PSTATE_SG: u8 = 2;
pub const PSTATE_SNOVICE: u8 = 4;
pub const PSTATE_TK: u8 = 8;

/// party.cpp's statics: the xy timer, party_create_byscript, party_booking_nextid.
#[table(accessor = party_state)]
pub struct PartyState {
    #[primary_key]
    pub key: u8,
    pub xy_timer: u64,
    pub create_byscript: bool,
    pub booking_nextid: u32,
}

/// party.hpp struct party_booking_ad_info (party_booking_db, keyed by char id).
#[table(accessor = party_booking)]
#[derive(Clone, Debug)]
pub struct PartyBooking {
    #[primary_key]
    pub char_id: u32,
    pub index: u32,
    pub charname: String,
    pub starttime: i32,
    pub level: i16,
    pub mapid: i16,
    pub job: Vec<i16>,
}

/// conf/char_athena.conf `server_name: rAthena` (chrif.cpp charserver_name, the booking rows' world_name).
pub const CHARSERVER_NAME: &str = "rAthena";

/// map.cpp `partybookings_table: party_bookings` — written by rAthena's web server (the recruitment board), read by
/// party_booking_load. Loki has no web server, so nothing writes rows here; party_booking_load finds none.
#[table(accessor = party_bookings)]
#[derive(Clone, Debug)]
pub struct PartyBookingsRow {
    #[primary_key]
    pub char_id: u32,
    pub world_name: String,
    pub account_id: u32,
    pub minimum_level: u16,
    pub maximum_level: u16,
}

fn state(c: &Ctx) -> PartyState {
    c.db().party_state().key().find(0).unwrap_or(PartyState { key: 0, xy_timer: 0, create_byscript: false, booking_nextid: 1 })
}

fn put_state(c: &Ctx, s: PartyState) {
    if c.db().party_state().key().find(0).is_some() {
        c.db().party_state().key().update(s);
    } else {
        c.db().party_state().insert(s);
    }
}

/// do_init_party: add_timer_interval(gettick() + party_update_interval, party_send_xy_timer, …). The module arms it
/// the first time the map server learns about a party (and re-arms it if the row is ever gone).
pub fn ensure_timer(c: &Ctx) {
    let mut s = state(c);
    if s.xy_timer != 0 && timer::get(c, s.xy_timer).is_some() {
        return;
    }
    s.xy_timer = timer::add_interval(c, c.tick() + party_update_interval(), Func::PartySendXy, 0, 0, party_update_interval());
    put_state(c, s);
}

/// common/db.cpp iteration order of an int-keyed DBMap (party_db, pc_db, channel users…): bucket (key % 283), then key.
pub fn idb_order(id: u32) -> (u32, u32) {
    (id % 283, id)
}

// ---------------------------------------------------------------------------------------------- lookups

/// party.cpp party_search.
pub fn party_search(c: &Ctx, party_id: u32) -> Option<PartyData> {
    if party_id == 0 {
        return None;
    }
    c.db().party_data().party_id().find(party_id)
}

fn put(c: &Ctx, p: &PartyData) {
    if c.db().party_data().party_id().find(p.party_id).is_some() {
        c.db().party_data().party_id().update(p.clone());
    } else {
        c.db().party_data().insert(p.clone());
    }
}

/// party_data->instance_id = instance_id (instance.rs).
pub fn set_instance_id(c: &Ctx, party_id: u32, instance_id: i32) {
    if let Some(mut p) = party_search(c, party_id) {
        p.instance_id = instance_id;
        put(c, &p);
    }
}

/// All map-server parties in party_db iteration order.
fn party_db_iter(c: &Ctx) -> Vec<PartyData> {
    let mut v: Vec<PartyData> = c.db().party_data().iter().collect();
    v.sort_by_key(|p| idb_order(p.party_id));
    v
}

/// party.cpp party_searchname (strncmpi over NAME_LENGTH).
pub fn party_searchname(c: &Ctx, name: &str) -> Option<PartyData> {
    let key: Vec<u8> = latin1(name).into_iter().take(NAME_LENGTH).collect();
    party_db_iter(c).into_iter().find(|p| {
        let n: Vec<u8> = latin1(&p.party.name).into_iter().take(NAME_LENGTH).collect();
        n.eq_ignore_ascii_case(&key)
    })
}

/// sd->status.party_id.
pub fn party_id_of(c: &Ctx, aid: u32) -> u32 {
    pc::character_of(c, aid).map(|ch| ch.party_id).unwrap_or(0)
}

/// sd->status.party_id = party_id.
fn set_party_id(c: &Ctx, aid: u32, party_id: u32) {
    if let Some(mut ch) = pc::character_of(c, aid) {
        if ch.party_id != party_id {
            ch.party_id = party_id;
            c.db().character().id().update(ch);
        }
    }
}

/// The party name a player's name tag shows (clif_name / strcharinfo(1)): "" when not in a party.
pub fn party_name_of(c: &Ctx, aid: u32) -> String {
    party_search(c, party_id_of(c, aid)).map(|p| p.party.name).unwrap_or_default()
}

/// party.cpp party_getmemberid: the member slot of this character, or None.
pub fn party_getmemberid(p: &PartyData, account_id: u32, char_id: u32) -> Option<usize> {
    p.party.member.iter().position(|m| m.account_id == account_id && m.char_id == char_id)
}

/// party.cpp party_getavailablesd: the first online member.
pub fn party_getavailablesd(p: &PartyData) -> Option<u32> {
    p.data.iter().find(|d| d.sd != 0).map(|d| d.sd)
}

/// The account ids of the party's online members (p->data[i].sd), in member order.
pub fn online_members(p: &PartyData) -> Vec<u32> {
    p.data.iter().filter(|d| d.sd != 0).map(|d| d.sd).collect()
}

/// Is `aid` the account of an online member (map_id2sd) whose client is there (session_isActive)?
fn session_active(c: &Ctx, aid: u32) -> bool {
    pc::get(c, aid).map(|p| !p.disconnected).unwrap_or(false)
}

/// party.cpp party_fill_member: a party_member for this player.
fn party_fill_member(c: &Ctx, aid: u32, leader: bool) -> Option<PartyMember> {
    let p = pc::get(c, aid)?;
    let ch = pc::character_of(c, aid)?;
    let map = p.mapindex.and_then(|m| crate::data::maps().get(m)).map(|d| d.name.clone()).unwrap_or_default();
    Some(PartyMember {
        account_id: ch.account_id,
        char_id: ch.id,
        name: ch.name.clone(),
        class: ch.class,
        map,
        lv: ch.base_level,
        online: true,
        leader,
    })
}

/// party.cpp party_sd_check: the online member's sd (account id), 0 when not online; auto-joins a player without a
/// party and kicks one that belongs to another.
fn party_sd_check(c: &Ctx, party_id: u32, account_id: u32, char_id: u32) -> u32 {
    let Some(p) = pc::get(c, account_id) else { return 0 };
    if p.char_id != char_id {
        return 0;
    }
    let mut pid = party_id_of(c, account_id);
    if pid == 0 {
        set_party_id(c, account_id, party_id); // auto-join if not in a party
        pid = party_id;
    }
    if pid != party_id {
        // If player belongs to a different party, kick him out.
        let name = pc::character_of(c, account_id).map(|ch| ch.name).unwrap_or_default();
        intif_party_leave(party_id, account_id, char_id, &name, PARTY_MEMBER_WITHDRAW_LEAVE);
        return 0;
    }
    account_id
}

/// party.cpp party_check_state.
pub(crate) fn party_check_state(p: &mut PartyData) {
    p.state = 0;
    for m in p.party.member.iter() {
        if !m.online {
            continue; // Those not online shouldn't aport to skill usage and all that.
        }
        match m.class {
            15 | 4038 | 4016 | 4070 | 4077 | 4106 | 4262 => p.state |= PSTATE_MONK, // MONK, BABY_MONK, CHAMPION, SURA, SURA_T, BABY_SURA, INQUISITOR
            4047 | 4226 | 4239 | 4241 | 4302 => p.state |= PSTATE_SG, // STAR_GLADIATOR, BABY_STAR_GLADIATOR, STAR_EMPEROR, BABY_STAR_EMPEROR, SKY_EMPEROR
            23 | 4045 | 4190 | 4191 | 4307 => p.state |= PSTATE_SNOVICE, // SUPER_NOVICE, SUPER_BABY, SUPER_NOVICE_E, SUPER_BABY_E, HYPER_NOVICE
            4046 | 4225 => p.state |= PSTATE_TK, // TAEKWON, BABY_TAEKWON
            _ => {}
        }
    }
}

// ---------------------------------------------------------------------------------------------- intif requests

/// intif.cpp intif_party_leave.
pub fn intif_party_leave(party_id: u32, account_id: u32, char_id: u32, name: &str, kind: u8) {
    intif::send(Inter::PartyLeave { party_id, account_id, char_id, name: trunc_name(name), kind });
}

/// intif.cpp intif_party_changemap (instance maps report their source map: map_getmapdata(instance_src_map)->index).
fn intif_party_changemap(c: &Ctx, aid: u32, online: bool) {
    let Some(p) = pc::get(c, aid) else { return };
    let Some(ch) = pc::character_of(c, aid) else { return };
    let m = p.mapindex.map(|m| crate::instance_map::instance_src_map(c, m).unwrap_or(m));
    let map = m.and_then(|m| crate::data::maps().get(m)).map(|d| d.name.clone()).unwrap_or_default();
    intif::send(Inter::PartyChangeMap { party_id: ch.party_id, account_id: ch.account_id, char_id: ch.id, online: online as u8, lv: ch.base_level, map });
}

/// safestrncpy(…, NAME_LENGTH).
fn trunc_name(s: &str) -> String {
    from_latin1(&latin1(s).into_iter().take(NAME_LENGTH - 1).collect::<Vec<u8>>())
}

// ---------------------------------------------------------------------------------------------- party.cpp

/// party.cpp party_create: 1 sent to the char server, 0 empty name, -2 already in (or joining / creating) a party.
pub fn party_create(c: &Ctx, aid: u32, name: &[u8], item: i32, item2: i32) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return 0 };
    // safestrncpy(tname, name, NAME_LENGTH); trim(tname)
    let tname: Vec<u8> = name.iter().copied().take_while(|&b| b != 0).take(NAME_LENGTH - 1).collect();
    if trim(&tname).is_empty() {
        return 0; // empty name
    }
    let mut x = social::sx(c, aid);
    // already associated with a party
    if party_id_of(c, aid) > 0 || x.party_joining || x.party_creating {
        clif_party_created(c, &sd, 2);
        return -2;
    }
    x.party_creating = true;
    social::put_sx(c, &x);
    let Some(leader) = party_fill_member(c, aid, true) else { return 0 };
    // intif_create_party sends the name as given (untrimmed), safestrncpy'd to NAME_LENGTH
    let name = from_latin1(&tname);
    intif::send(Inter::CreateParty { name, item: item as u8, item2: item2 as u8, leader });
    1
}

/// strlib.cpp trim (C-locale isspace at both ends).
fn trim(b: &[u8]) -> Vec<u8> {
    let mut s = 0;
    let mut e = b.len();
    while s < e && crate::cstr::is_space(b[s]) {
        s += 1;
    }
    while e > s && crate::cstr::is_space(b[e - 1]) {
        e -= 1;
    }
    b[s..e].to_vec()
}

/// party.cpp party_created (the char server's answer).
pub fn party_created(c: &Ctx, account_id: u32, char_id: u32, fail: i32, party_id: u32, _name: &str) {
    let mut x = social::sx(c, account_id);
    let ok = pc::get(c, account_id).map(|p| p.char_id == char_id).unwrap_or(false) && x.party_creating;
    if !ok {
        // Character logged off before creation ack?
        if fail == 0 {
            // break up party since player could not be added to it.
            intif_party_leave(party_id, account_id, char_id, "", PARTY_MEMBER_WITHDRAW_LEAVE);
        }
        return;
    }
    x.party_creating = false;
    social::put_sx(c, &x);
    let Some(sd) = unit::get(c, account_id) else { return };
    if fail == 0 {
        set_party_id(c, account_id, party_id);
        clif_party_created(c, &sd, 0); // Success message
        crate::achievement::achievement_update_objective(c, Some(account_id), crate::db::misc::achievement::AG_PARTY, &[1]);
        // We don't do any further work here because the char-server sends a party info packet right after creating the party
        let mut s = state(c);
        if s.create_byscript {
            // returns party id in $@party_create_id if party is created by script
            mapreg_setreg(c, "$@party_create_id", 0, party_id as i64);
            s.create_byscript = false;
            put_state(c, s);
        }
    } else {
        clif_party_created(c, &sd, 1); // "party name already exists"
    }
}

/// party.cpp party_request_info.
pub fn party_request_info(party_id: u32, char_id: u32) {
    intif::send(Inter::PartyInfo { party_id, char_id });
}

/// party.cpp party_recv_noinfo: the char server does not know this party.
pub fn party_recv_noinfo(c: &Ctx, party_id: u32, char_id: u32) {
    party_broken(c, party_id);
    if char_id != 0 {
        // requester
        if let Some(sd) = pc::charid2sd(c, char_id) {
            if party_id_of(c, sd.id) == party_id {
                set_party_id(c, sd.id, 0);
            }
        }
    }
}

/// party.cpp party_recv_info: the char server's copy of a party arrived (creation, a member added or removed, a
/// rename, or an info request).
pub fn party_recv_info(c: &Ctx, sp: &Party, char_id: u32) {
    ensure_timer(c);
    let mut removed: Vec<usize> = Vec::new(); // member_id in old data
    let mut added: Vec<usize> = Vec::new(); // member_id in new data
    let mut rename = false;
    match party_search(c, sp.party_id) {
        Some(p) => {
            // diff members
            for (member_id, member) in p.party.member.iter().enumerate() {
                if member.char_id == 0 {
                    continue; // empty
                }
                match sp.member.iter().position(|m| m.account_id == member.account_id && m.char_id == member.char_id) {
                    None => removed.push(member_id),
                    // If the member already existed, compare the old to the (possible) new name
                    Some(i) => {
                        if !rename && member.name != sp.member[i].name {
                            rename = true;
                        }
                    }
                }
            }
            for (member_id, member) in sp.member.iter().enumerate() {
                if member.char_id == 0 {
                    continue; // empty
                }
                if !p.party.member.iter().any(|m| m.account_id == member.account_id && m.char_id == member.char_id) {
                    added.push(member_id);
                }
            }
        }
        None => {
            for (member_id, m) in sp.member.iter().enumerate() {
                if m.char_id != 0 {
                    added.push(member_id);
                }
            }
            put(c, &PartyData { party_id: sp.party_id, party: Party::empty(sp.party_id), data: vec![PartyMemberData::default(); MAX_PARTY], itemc: 0, instance_id: 0, state: 0 });
        }
    }
    while let Some(member_id) = removed.pop() {
        // no longer in party
        let Some(p) = party_search(c, sp.party_id) else { break };
        let sd = p.data[member_id].sd;
        if sd == 0 {
            continue; // not online
        }
        let Some(ch) = pc::character_of(c, sd) else { continue };
        party_member_withdraw(c, sp.party_id, ch.account_id, ch.id, &ch.name, PARTY_MEMBER_WITHDRAW_LEAVE);
    }
    let Some(mut p) = party_search(c, sp.party_id) else { return };
    p.party = sp.clone();
    p.state = 0;
    p.data = vec![PartyMemberData::default(); MAX_PARTY];
    put(c, &p);
    for member_id in 0..MAX_PARTY {
        let m = p.party.member[member_id].clone();
        if m.char_id == 0 {
            continue; // empty
        }
        p.data[member_id].sd = party_sd_check(c, sp.party_id, m.account_id, m.char_id);
    }
    party_check_state(&mut p);
    put(c, &p);
    while let Some(member_id) = added.pop() {
        // new in party
        let Some(p) = party_search(c, sp.party_id) else { break };
        let sd = p.data[member_id].sd;
        if sd == 0 {
            continue; // not online
        }
        let Some(su) = unit::get(c, sd) else { continue };
        clif::name_area(c, &su); // Update other people's display. [Skotlex]
        clif_party_member_info(c, &p, sd);
        // Only send this on party creation, otherwise it will be sent by party_send_movemap [Lemongrass]
        if social::sx(c, sd).party_creating {
            clif_party_option(c, &p, Some(sd), 0x100);
        }
        clif_party_info(c, &p, None);
        if p.instance_id > 0 {
            crate::instance::instance_reqinfo(c, sd, p.instance_id);
        }
    }
    // If a player was renamed, make sure to resend the party information
    if rename {
        if let Some(p) = party_search(c, sp.party_id) {
            clif_party_info(c, &p, None);
        }
    }
    if char_id != 0 {
        // requester
        if let Some(sd) = pc::charid2sd(c, char_id) {
            if party_id_of(c, sd.id) == sp.party_id {
                let p = party_search(c, sp.party_id);
                if p.map(|p| party_getmemberid(&p, sd.id, sd.char_id).is_none()).unwrap_or(true) {
                    set_party_id(c, sd.id, 0); // was not in the party
                }
            }
        }
    }
}

/// party.cpp party_invite: `aid` (a leader) invites `tsd`.
pub fn party_invite(c: &Ctx, aid: u32, tsd: Option<u32>) -> bool {
    let Some(sd) = unit::get(c, aid) else { return false };
    let Some(p) = party_search(c, party_id_of(c, aid)) else { return false };
    // confirm if this player is a party leader
    match p.data.iter().position(|d| d.sd == aid) {
        Some(i) if p.party.member[i].leader => {}
        _ => {
            clif::displaymessage(c, &sd, crate::data::msg_txt(282));
            return false;
        }
    }
    // Party locked.
    if crate::npc::mapflag(c, sd.map, crate::npc::MF_PARTYLOCK) {
        clif::displaymessage(c, &sd, crate::data::msg_txt(227));
        return false;
    }
    if p.instance_id > 0 && instance_block_invite() {
        clif_party_invite_reply(c, &sd, "", PARTY_REPLY_MEMORIALDUNGEON);
        return false;
    }
    let Some(tu) = tsd.and_then(|t| unit::get(c, t)) else {
        clif_party_invite_reply(c, &sd, "", PARTY_REPLY_OFFLINE);
        return false;
    };
    let tch = pc::character_of(c, tu.id);
    let tname = tch.as_ref().map(|ch| ch.name.clone()).unwrap_or_default();
    if tch.as_ref().map(|ch| ch.disable_partyinvite != 0).unwrap_or(false) {
        clif_party_invite_reply(c, &sd, &tname, PARTY_REPLY_JOINMSG_REFUSE);
        return false;
    }
    let mut tx = social::sx(c, tu.id);
    // @noask [LuzZza]
    if tx.noask {
        social::clif_noask_sub(c, &sd, &tu, 394); // Autorejected party invite from %s.
        return false;
    }
    if block_account_in_same_party() && p.party.member.iter().any(|m| m.account_id == tu.id) {
        clif_party_invite_reply(c, &sd, &tname, PARTY_REPLY_DUAL);
        return false;
    }
    // confirm if there is an open slot in the party
    if !p.party.member.iter().any(|m| m.account_id == 0) {
        clif_party_invite_reply(c, &sd, &tname, PARTY_REPLY_FULL);
        return false;
    }
    // confirm whether the account has the ability to invite before checking the player
    if !pc::has_permission(c, aid, crate::rules::PC_PERM_PARTY) || !pc::has_permission(c, tu.id, crate::rules::PC_PERM_PARTY) {
        clif::displaymessage(c, &sd, crate::data::msg_txt(81)); // Your GM level doesn't authorize you to perform this action on the specified player.
        return false;
    }
    if !invite_request_check() {
        // (guild_invite / trading / adopt_invite: with their systems) — invite_request_check is on
    }
    // You can't invite someone who has already disconnected.
    if !session_active(c, tu.id) {
        clif_party_invite_reply(c, &sd, &tname, PARTY_REPLY_REJECTED);
        return false;
    }
    // Already associated with a party
    if party_id_of(c, tu.id) > 0 || tx.party_invite > 0 {
        clif_party_invite_reply(c, &sd, &tname, PARTY_REPLY_JOIN_OTHER_PARTY);
        return false;
    }
    tx.party_invite = p.party_id;
    tx.party_invite_account = aid;
    social::put_sx(c, &tx);
    clif_party_invite(c, &sd, &tu);
    true
}

/// party.cpp party_isleader.
pub fn party_isleader(c: &Ctx, aid: u32) -> bool {
    let Some(ch) = pc::character_of(c, aid) else { return false };
    if ch.party_id == 0 {
        return false;
    }
    let Some(p) = party_search(c, ch.party_id) else { return false };
    p.party.member.iter().find(|m| m.char_id == ch.id).map(|m| m.leader).unwrap_or(false)
}

/// party.cpp party_join: `aid` joins without an invitation (party booking accepted by the leader).
pub fn party_join(c: &Ctx, aid: u32, party_id: u32) {
    // Player is in a party already now
    if party_id_of(c, aid) != 0 {
        return;
    }
    let mut x = social::sx(c, aid);
    // Player is already associated with a party
    if x.party_creating || x.party_joining {
        return;
    }
    let Some(p) = party_search(c, party_id) else { return };
    // Player is in the party with a different character already
    if block_account_in_same_party() && p.party.member.iter().any(|m| m.account_id == aid) {
        return;
    }
    // Party is already full
    if !p.party.member.iter().any(|m| m.account_id == 0) {
        return;
    }
    x.party_joining = true;
    social::put_sx(c, &x);
    let Some(member) = party_fill_member(c, aid, false) else { return };
    intif::send(Inter::PartyAddMember { party_id, member });
}

/// party.cpp party_booking_load: the recruitment requirements the web server stored for this leader.
pub fn party_booking_load(c: &Ctx, account_id: u32, char_id: u32) -> Option<(u16, u16)> {
    // `WHERE world_name = charserver_name AND account_id AND char_id`
    c.db()
        .party_bookings()
        .char_id()
        .find(char_id)
        .filter(|r| r.account_id == account_id && r.world_name == CHARSERVER_NAME)
        .map(|r| (r.minimum_level, r.maximum_level))
}

/// party.cpp party_reply_invite (party_add_member is this with flag 1).
pub fn party_reply_invite(c: &Ctx, aid: u32, party_id: u32, flag: i32) -> bool {
    let mut x = social::sx(c, aid);
    // forged
    if x.party_invite != party_id {
        x.party_invite = 0;
        x.party_invite_account = 0;
        social::put_sx(c, &x);
        return false;
    }
    // The character is already in a party, possibly left a party invite open and created his own party
    if party_id_of(c, aid) != 0 {
        // On Aegis no rejection packet is sent to the inviting player
        x.party_invite = 0;
        x.party_invite_account = 0;
        social::put_sx(c, &x);
        return false;
    }
    // accepted and allowed
    if flag == 1 && !x.party_creating && !x.party_joining {
        if let Some(p) = party_search(c, party_id) {
            if p.instance_id > 0 && instance_block_invite() {
                x.party_invite = 0;
                x.party_invite_account = 0;
                social::put_sx(c, &x);
                return false;
            }
        }
        x.party_joining = true;
        social::put_sx(c, &x);
        let Some(member) = party_fill_member(c, aid, false) else { return false };
        intif::send(Inter::PartyAddMember { party_id: x.party_invite, member });
        return true;
    }
    // rejected or failure
    let tsd = x.party_invite_account;
    x.party_invite = 0;
    x.party_invite_account = 0;
    social::put_sx(c, &x);
    if let Some(tu) = unit::get(c, tsd).filter(|u| pc::get(c, u.id).is_some()) {
        let name = pc::character_of(c, aid).map(|ch| ch.name).unwrap_or_default();
        clif_party_invite_reply(c, &tu, &name, PARTY_REPLY_REJECTED);
    }
    false
}

/// party.cpp party_member_joined (pc_reg_received): the player is online and belongs to a party.
pub fn party_member_joined(c: &Ctx, aid: u32) {
    let Some(ch) = pc::character_of(c, aid) else { return };
    let Some(mut p) = party_search(c, ch.party_id) else {
        party_request_info(ch.party_id, ch.id);
        return;
    };
    ensure_timer(c);
    match p.party.member.iter().position(|m| m.account_id == ch.account_id && m.char_id == ch.id) {
        Some(i) => {
            p.data[i].sd = aid;
            put(c, &p);
        }
        None => set_party_id(c, aid, 0), // He does not belongs to the party really?
    }
}

/// party.cpp party_member_added (the char server's answer to an add request; flag 1 = failure).
pub fn party_member_added(c: &Ctx, party_id: u32, account_id: u32, char_id: u32, flag: u8) {
    let mut x = social::sx(c, account_id);
    let ok = pc::get(c, account_id).map(|p| p.char_id == char_id).unwrap_or(false) && x.party_joining;
    if !ok {
        if flag == 0 {
            // Char logged off before being accepted into party.
            intif_party_leave(party_id, account_id, char_id, "", PARTY_MEMBER_WITHDRAW_LEAVE);
        }
        return;
    }
    let sd2 = x.party_invite_account;
    x.party_joining = false;
    x.party_invite = 0;
    x.party_invite_account = 0;
    social::put_sx(c, &x);
    let Some(p) = party_search(c, party_id) else {
        log::error!("party_member_added: party {party_id} not found.");
        intif_party_leave(party_id, account_id, char_id, "", PARTY_MEMBER_WITHDRAW_LEAVE);
        return;
    };
    let sd2u = unit::get(c, sd2).filter(|u| pc::get(c, u.id).is_some());
    let name = pc::character_of(c, account_id).map(|ch| ch.name).unwrap_or_default();
    if flag != 0 {
        // failed
        if let Some(s2) = &sd2u {
            clif_party_invite_reply(c, s2, &name, PARTY_REPLY_FULL);
        }
        return;
    }
    set_party_id(c, account_id, party_id);
    let Some(sd) = unit::get(c, account_id) else { return };
    clif_party_member_info(c, &p, account_id);
    clif_party_option(c, &p, Some(account_id), 0x100);
    clif_party_info(c, &p, Some(account_id));
    if let Some(s2) = &sd2u {
        clif_party_invite_reply(c, s2, &name, PARTY_REPLY_ACCEPTED);
    }
    for d in p.data.iter() {
        // hp of the other party members
        if d.sd == 0 {
            continue;
        }
        let Some(o) = unit::get(c, d.sd) else { continue };
        if o.id != account_id && o.char_id != char_id {
            // sd2->battle_status.hp / max_hp
            let (hp, max_hp) = crate::status::status_data_of(c, o.id).map(|s| (s.hp, s.max_hp)).unwrap_or((o.hp as u32, o.max_hp as u32));
            crate::status::clif::hpmeter_single(c, &sd, o.id, hp, max_hp);
        }
    }
    clif_party_hp(c, &sd);
    clif_party_xy(c, &sd);
    clif::name_area(c, &sd); // Update char name's display [Skotlex]
    if p.instance_id > 0 {
        crate::instance::instance_reqinfo(c, account_id, p.instance_id);
    }
}

/// party.cpp party_removemember: the leader `aid` expels <account_id, name>.
pub fn party_removemember(c: &Ctx, aid: u32, account_id: u32, name: &str) -> bool {
    let Some(sd) = unit::get(c, aid) else { return false };
    // Party locked.
    if crate::npc::mapflag(c, sd.map, crate::npc::MF_PARTYLOCK) {
        clif::displaymessage(c, &sd, crate::data::msg_txt(227));
        return false;
    }
    let Some(p) = party_search(c, party_id_of(c, aid)) else { return false };
    if p.instance_id > 0 && instance_block_expulsion() {
        return false;
    }
    // check the requesting char's party membership
    let Some(i) = party_getmemberid(&p, aid, sd.char_id) else { return false }; // request from someone not in party? o.O
    if !p.party.member[i].leader {
        return false; // only party leader may remove members
    }
    let nb: Vec<u8> = latin1(name).into_iter().take(NAME_LENGTH).collect();
    let Some(i) = p.party.member.iter().position(|m| m.account_id == account_id && latin1(&m.name).into_iter().take(NAME_LENGTH).eq(nb.iter().copied())) else {
        return false; // no such char in party
    };
    crate::trade::trade_bound_cancel(c, aid, crate::inventory::BOUND_PARTY); // party_trade_bound_cancel(sd)
    let m = &p.party.member[i];
    intif_party_leave(p.party_id, account_id, m.char_id, &m.name, PARTY_MEMBER_WITHDRAW_EXPEL);
    true
}

/// party.cpp party_removemember2 (script party_delmember): -3 not in a party, -2 no party, -1 no member, 1 sent.
pub fn party_removemember2(c: &Ctx, sd: Option<u32>, char_id: u32, party_id: u32) -> i32 {
    match sd {
        Some(aid) => {
            let Some(ch) = pc::character_of(c, aid) else { return -3 };
            if ch.party_id == 0 {
                return -3;
            }
            crate::trade::trade_bound_cancel(c, aid, crate::inventory::BOUND_PARTY); // party_trade_bound_cancel
            intif_party_leave(ch.party_id, ch.account_id, ch.id, &ch.name, PARTY_MEMBER_WITHDRAW_EXPEL);
            1
        }
        None => {
            let Some(p) = party_search(c, party_id) else { return -2 };
            let Some(i) = p.party.member.iter().position(|m| m.char_id == char_id) else { return -1 };
            let m = &p.party.member[i];
            intif_party_leave(party_id, m.account_id, char_id, &m.name, PARTY_MEMBER_WITHDRAW_EXPEL);
            1
        }
    }
}

/// party.cpp party_leave: the player asks to leave the party.
pub fn party_leave(c: &Ctx, aid: u32, show_message: bool) -> bool {
    let Some(sd) = unit::get(c, aid) else { return false };
    // Party locked.
    if crate::npc::mapflag(c, sd.map, crate::npc::MF_PARTYLOCK) {
        // If it was not triggered by the user itself, but from a script for example
        if show_message {
            clif::displaymessage(c, &sd, crate::data::msg_txt(227));
        }
        return false;
    }
    let Some(ch) = pc::character_of(c, aid) else { return false };
    let Some(p) = party_search(c, ch.party_id) else { return false };
    if p.instance_id > 0 && instance_block_leave() {
        if show_message {
            clif_party_withdraw(c, &sd, aid, &ch.name, PARTY_MEMBER_WITHDRAW_CANT_LEAVE, PTarget::SelfOnly);
        }
        return false;
    }
    if party_getmemberid(&p, aid, ch.id).is_none() {
        return false;
    }
    crate::trade::trade_bound_cancel(c, aid, crate::inventory::BOUND_PARTY); // party_trade_bound_cancel
    intif_party_leave(p.party_id, aid, ch.id, &ch.name, PARTY_MEMBER_WITHDRAW_LEAVE);
    true
}

/// party.cpp party_member_withdraw (the char server's notice that a member left).
pub fn party_member_withdraw(c: &Ctx, party_id: u32, account_id: u32, char_id: u32, name: &str, kind: u8) {
    let sd = pc::charid2sd(c, char_id);
    if let Some(mut p) = party_search(c, party_id) {
        if let Some(party_sd) = party_getavailablesd(&p).and_then(|a| unit::get(c, a)) {
            clif_party_withdraw(c, &party_sd, account_id, name, kind, PTarget::Party);
        }
        if let Some(i) = p.party.member.iter().position(|m| m.account_id == account_id && m.char_id == char_id) {
            p.party.member[i] = PartyMember::default();
            p.data[i] = PartyMemberData::default();
            p.party.count = p.party.count.wrapping_sub(1);
            party_check_state(&mut p);
            put(c, &p);
        }
    }
    if let Some(sd) = sd {
        if party_id_of(c, sd.id) == party_id {
            crate::trade::trade_bound_cancel(c, sd.id, crate::inventory::BOUND_PARTY); // party_trade_bound_cancel
            // BOUND_ITEMS: the party-bound items go (party.cpp:828-832)
            for i in crate::inventory::pc_bound_chk(c, sd.id, crate::inventory::BOUND_PARTY) {
                let amount = crate::inventory::inv_get(c, sd.id, i).map(|r| r.item.amount as i32).unwrap_or(0);
                crate::inventory::pc_delitem(c, sd.id, i as i32, amount, 0, 1, crate::map_log::LOG_TYPE_BOUND_REMOVAL);
            }
            set_party_id(c, sd.id, 0);
            if let Some(sd) = unit::get(c, sd.id) {
                clif::name_area(c, &sd); // Update name display [Skotlex]
            }
            // TODO: hp bars should be cleared too
            if let Some(p) = party_search(c, party_id).filter(|p| p.instance_id != 0) {
                // User was on the instance map of the party
                let m = unit::get(c, sd.id).map(|u| u.map).unwrap_or(0);
                if p.instance_id == crate::instance_map::map_instance_id(c, m) {
                    crate::instance_map::pc_setpos_savepoint(c, sd.id, crate::unit::CLR_TELEPORT);
                }
            }
        }
    }
}

/// party.cpp party_broken (the char server's notice that the party is gone): 1 when it existed.
pub fn party_broken(c: &Ctx, party_id: u32) -> i32 {
    let Some(p) = party_search(c, party_id) else { return 0 };
    if p.instance_id != 0 {
        crate::instance::instance_destroy(c, p.instance_id);
    }
    let Some(p) = party_search(c, party_id) else { return 0 };
    for i in 0..MAX_PARTY {
        let sd = p.data[i].sd;
        if sd != 0 {
            if let Some(u) = unit::get(c, sd) {
                clif_party_withdraw(c, &u, p.party.member[i].account_id, &p.party.member[i].name, PARTY_MEMBER_WITHDRAW_EXPEL, PTarget::SelfOnly);
            }
            set_party_id(c, sd, 0);
        }
    }
    c.db().party_data().party_id().delete(party_id);
    1
}

/// party.cpp party_changeoption.
pub fn party_changeoption(c: &Ctx, aid: u32, exp: i32, item: i32) -> i32 {
    let pid = party_id_of(c, aid);
    if pid == 0 {
        return -3;
    }
    intif::send(Inter::PartyChangeOption { party_id: pid, account_id: aid, exp: exp as u16, item: item as u16 });
    0
}

/// party.cpp party_setoption (script party_changeoption): options 0 exp, 1 item share, 2 pickup distribution.
pub fn party_setoption(_c: &Ctx, p: &PartyData, option: i32, flag: i32) -> i32 {
    let Some(i) = p.party.member.iter().position(|m| m.leader) else { return 0 };
    let leader = p.party.member[i].account_id;
    let item = p.party.item as i32;
    let (exp, item) = match option {
        0 => (flag, item),
        1 => (p.party.exp as i32, if flag != 0 { item | 1 } else { item & !1 }),
        2 => (p.party.exp as i32, if flag != 0 { item | 2 } else { item & !2 }),
        _ => return 0,
    };
    intif::send(Inter::PartyChangeOption { party_id: p.party_id, account_id: leader, exp: exp as u16, item: item as u16 });
    1
}

/// party.cpp party_optionchanged (the char server's answer: flag & 1 exp change denied, & 0x10 item change denied).
pub fn party_optionchanged(c: &Ctx, party_id: u32, account_id: u32, exp: u16, item: u16, flag: u8) {
    let Some(mut p) = party_search(c, party_id) else { return };
    let sd = pc::get(c, account_id).map(|_| account_id);
    // Flag&1: Exp change denied. Flag&2: Item change denied.
    if flag & 0x01 == 0 && p.party.exp as u16 != exp {
        p.party.exp = (exp & 1) as u8;
    }
    if flag & 0x10 == 0 && p.party.item as u16 != item {
        p.party.item = (item & 3) as u8;
    }
    put(c, &p);
    clif_party_option(c, &p, sd, flag as i32);
}

/// party.cpp party_changeleader: `sd` makes `tsd` the leader (with `p` given: a script, leader may be offline).
/// Returns 1 on success, 0 / negative on failure.
pub fn party_changeleader(c: &Ctx, sd: Option<u32>, tsd: Option<u32>, p: Option<PartyData>) -> i32 {
    let (mut p, mi, tmi) = match p {
        None => {
            let Some(aid) = sd else { return -1 };
            let Some(su) = unit::get(c, aid) else { return -1 };
            let pid = party_id_of(c, aid);
            if pid == 0 {
                return -1;
            }
            if tsd.map(|t| party_id_of(c, t) != pid).unwrap_or(true) || tsd.and_then(|t| pc::get(c, t)).is_none() {
                clif::displaymessage(c, &su, crate::data::msg_txt(283));
                return -3;
            }
            if crate::npc::mapflag(c, su.map, crate::npc::MF_PARTYLOCK) {
                clif::displaymessage(c, &su, crate::data::msg_txt(287));
                return 0;
            }
            let Some(p) = party_search(c, pid) else { return -1 };
            if p.instance_id > 0 && instance_block_leaderchange() {
                return 0;
            }
            let Some(mi) = p.data.iter().position(|d| d.sd == aid) else { return 0 }; // Shouldn't happen
            if !p.party.member[mi].leader {
                // Need to be a party leader.
                clif::displaymessage(c, &su, crate::data::msg_txt(282));
                return 0;
            }
            let t = tsd.unwrap_or(0);
            let Some(tmi) = p.data.iter().position(|d| d.sd == t) else { return 0 }; // Shouldn't happen
            if change_party_leader_samemap() && p.party.member[mi].map != p.party.member[tmi].map {
                crate::clif_items::msg(c, &su, MSI_PARTY_MASTER_CHANGE_SAME_MAP);
                return 0;
            }
            (p, mi, tmi)
        }
        Some(p) => {
            let Some(mi) = p.party.member.iter().position(|m| m.leader) else { return 0 };
            let t = tsd.unwrap_or(0);
            let Some(tmi) = p.data.iter().position(|d| d.sd == t && t != 0) else { return 0 };
            (p, mi, tmi)
        }
    };
    // Change leadership.
    p.party.member[mi].leader = false;
    p.party.member[tmi].leader = true;
    put(c, &p);
    // Update members (p->data[mi].sd: the previous leader must be online here, as in rAthena)
    let old = p.data[mi].sd;
    let new = p.data[tmi].sd;
    match unit::get(c, old) {
        Some(ou) if old != 0 => clif_party_leaderchanged(c, &ou, old, new),
        _ => log::error!("party_changeleader: previous leader of party {} is not online", p.party_id),
    }
    // Update info.
    intif::send(Inter::PartyLeaderChange { party_id: p.party_id, account_id: p.party.member[tmi].account_id, char_id: p.party.member[tmi].char_id });
    clif_party_info(c, &p, None);
    1
}

/// party.cpp party_recv_movemap: a member changed maps, logged in or out (the char server's notice).
pub fn party_recv_movemap(c: &Ctx, party_id: u32, account_id: u32, char_id: u32, online: u8, lv: u16, map: &str) {
    let Some(mut p) = party_search(c, party_id) else { return };
    let Some(i) = p.party.member.iter().position(|m| m.account_id == account_id && m.char_id == char_id) else {
        log::error!("party_recv_movemap: char {account_id}/{char_id} not found in party {} (id:{party_id})", p.party.name);
        return;
    };
    p.party.member[i].map = map.to_string();
    p.party.member[i].online = online != 0;
    p.party.member[i].lv = lv;
    put(c, &p);
    // Check if they still exist on this map server
    let sd = party_sd_check(c, party_id, account_id, char_id);
    let Some(mut p) = party_search(c, party_id) else { return };
    p.data[i].sd = sd;
    put(c, &p);
    clif_party_info(c, &p, None);
}

/// party.cpp party_send_movemap (clif_parse_LoadEndAck): tell the char server where the member is, and the member
/// about the party on login.
pub fn party_send_movemap(c: &Ctx, aid: u32) {
    if party_id_of(c, aid) == 0 {
        return;
    }
    intif_party_changemap(c, aid, true);
    let Some(p) = party_search(c, party_id_of(c, aid)) else { return };
    let Some(pcd) = pc::get(c, aid) else { return };
    if pcd.connect_new {
        // Note that this works because this function is invoked before connect_new is cleared.
        clif_party_option(c, &p, Some(aid), 0x100);
        clif_party_info(c, &p, Some(aid));
        clif_party_member_info(c, &p, aid);
    }
    if !pcd.disconnected {
        // synchronize minimap positions with the rest of the party
        let Some(sd) = unit::get(c, aid) else { return };
        for d in p.data.iter() {
            if d.sd == 0 || d.sd == aid {
                continue;
            }
            let Some(o) = unit::get(c, d.sd) else { continue };
            if o.map == sd.map {
                clif_party_xy_single(c, &sd, &o);
                clif_party_xy_single(c, &o, &sd);
            }
        }
    }
}

/// party.cpp party_send_levelup: the level reaches the char server (status agent calls this on level up).
pub fn party_send_levelup(c: &Ctx, aid: u32) {
    intif_party_changemap(c, aid, true);
}

/// party.cpp party_send_logout (unit_free).
pub fn party_send_logout(c: &Ctx, aid: u32) -> i32 {
    let pid = party_id_of(c, aid);
    if pid == 0 {
        return 0;
    }
    intif_party_changemap(c, aid, false);
    let Some(mut p) = party_search(c, pid) else { return 0 };
    match p.data.iter().position(|d| d.sd == aid) {
        Some(i) => {
            p.data[i] = PartyMemberData::default();
            put(c, &p);
        }
        None => log::error!("party_send_logout: Failed to locate member {aid} in party {pid}!"),
    }
    1
}

/// party.cpp party_send_message (intif_party_message goes nowhere: there is no other map server).
pub fn party_send_message(c: &Ctx, aid: u32, mes: &[u8], len: usize) -> i32 {
    let pid = party_id_of(c, aid);
    if pid == 0 {
        return 0;
    }
    party_recv_message(c, pid, aid, mes, len);
    // Chat logging type 'P' / Party Chat
    crate::map_log::log_chat_sd(c, crate::map_log::LOG_CHAT_PARTY, pid as i32, aid, None, &crate::net::wire::from_latin1(crate::net::wire::until_nul(mes)));
    0
}

/// party.cpp party_recv_message.
pub fn party_recv_message(c: &Ctx, party_id: u32, account_id: u32, mes: &[u8], len: usize) -> i32 {
    let Some(p) = party_search(c, party_id) else { return 0 };
    clif_party_message(c, &p, account_id, mes, len);
    0
}

/// party.cpp party_send_xy_timer: each online member's position (and, with party_hp_mode 1, HP) when it changed.
pub fn party_send_xy_timer(c: &Ctx) {
    // for each existing party
    for p0 in party_db_iter(c) {
        if p0.party.count == 0 {
            continue; // no online party members so do not iterate
        }
        let pid = p0.party_id;
        // for each member of this party
        for i in 0..MAX_PARTY {
            let Some(p) = party_search(c, pid) else { break };
            let aid = p.data[i].sd;
            if aid == 0 {
                continue;
            }
            let Some(sd) = unit::get(c, aid) else { continue };
            let mut p = p;
            let mut changed = false;
            if p.data[i].x != sd.x as u16 || p.data[i].y != sd.y as u16 {
                // perform position update
                clif_party_xy(c, &sd);
                p.data[i].x = sd.x as u16;
                p.data[i].y = sd.y as u16;
                changed = true;
            }
            if party_hp_mode() != 0 && p.data[i].hp != sd.hp as u32 {
                // perform hp update
                clif_party_hp(c, &sd);
                p.data[i].hp = sd.hp as u32;
                changed = true;
            }
            if changed {
                put(c, &p);
            }
        }
    }
}

/// party.cpp party_send_xy_clear.
pub fn party_send_xy_clear(c: &Ctx, party_id: u32) {
    let Some(mut p) = party_search(c, party_id) else { return };
    for d in p.data.iter_mut() {
        if d.sd == 0 {
            continue;
        }
        d.hp = 0;
        d.x = 0;
        d.y = 0;
    }
    put(c, &p);
}

/// One member's share from [`exp_share`].
#[derive(Clone, Debug, PartialEq)]
pub struct ExpShare {
    pub aid: u32,
    pub base_exp: u64,
    pub job_exp: u64,
    pub zeny: i32,
}

/// mmo.hpp MAX_EXP.
pub const MAX_EXP: u64 = i64::MAX as u64;

/// map.hpp apply_rate: value * rate / 100 (as double for 64-bit values).
fn apply_rate(v: u64, rate: i32) -> f64 {
    v as f64 * rate as f64 / 100.0
}

fn cap_exp(v: f64, lo: u64, hi: u64) -> u64 {
    if v.is_nan() {
        return lo;
    }
    let v = if v < lo as f64 { lo as f64 } else if v > hi as f64 { hi as f64 } else { v };
    v as u64
}

/// party.cpp party_exp_share (RENEWAL_EXP): the party's share of a kill by a monster on map `src_map`. The members
/// who get a share are the online ones on that map who are alive (and not idle with idle_no_share); each gets
/// base/job/zeny divided by their number (party_even_share_bonus applied), then the renewal level penalty
/// `penalty(aid)` (pc_level_penalty_mod(sd, PENALTY_EXP, nullptr, md): 100 = none) on the EXP. `gain` receives each
/// member's result in member order — it must call pc_gainexp(sd, src, base, job, 0) and, when zeny is not 0,
/// pc_getzeny(sd, zeny, LOG_TYPE_PICKDROP_MONSTER) (both belong to the status / item systems). `src_is_mob` false
/// returns at once, as RENEWAL_EXP's `if (!md) return;`.
pub fn exp_share(c: &Ctx, party_id: u32, src_map: u16, src_is_mob: bool, base_exp: u64, job_exp: u64, zeny: i32, penalty: &mut dyn FnMut(u32) -> i32, gain: &mut dyn FnMut(&ExpShare)) {
    if !src_is_mob {
        return;
    }
    let Some(p) = party_search(c, party_id) else { return };
    // count the number of players eligible for exp sharing
    let mut sd: Vec<u32> = Vec::new();
    for d in p.data.iter() {
        if d.sd == 0 {
            continue;
        }
        let Some(u) = unit::get(c, d.sd) else { continue };
        if u.map != src_map || pc::isdead(c, d.sd) || (idle_no_share() && pc_isidle_party(c, d.sd)) {
            continue;
        }
        sd.push(d.sd);
    }
    let cnt = sd.len() as u64;
    if cnt < 1 {
        return;
    }
    let mut base_exp = base_exp / cnt;
    let mut job_exp = job_exp / cnt;
    let mut zeny = zeny / cnt as i32;
    if party_even_share_bonus() != 0 && cnt > 1 {
        let bonus = 100.0 + (party_even_share_bonus() * (cnt as i64 - 1)) as f64;
        if base_exp != 0 {
            base_exp = cap_exp(base_exp as f64 * bonus / 100.0, 0, MAX_EXP);
        }
        if job_exp != 0 {
            job_exp = cap_exp(job_exp as f64 * bonus / 100.0, 0, MAX_EXP);
        }
        if zeny != 0 {
            let z = zeny as f64 * bonus / 100.0;
            zeny = z.clamp(i32::MIN as f64, i32::MAX as f64) as i32;
        }
    }
    for aid in sd {
        let (mut base_gained, mut job_gained) = (base_exp, job_exp);
        if base_exp != 0 || job_exp != 0 {
            let rate = penalty(aid);
            if rate != 100 {
                if base_exp != 0 {
                    base_gained = cap_exp(apply_rate(base_exp, rate), 1, MAX_EXP);
                }
                if job_exp != 0 {
                    job_gained = cap_exp(apply_rate(job_exp, rate), 1, MAX_EXP);
                }
            }
        }
        gain(&ExpShare { aid, base_exp: base_gained, job_exp: job_gained, zeny });
    }
}

/// party.cpp party_exp_share as mob_dead calls it (monsters, phase 3): [`exp_share`] with its pc_gainexp(sd, src,
/// base, job, 0) (status/progress.rs) and pc_getzeny(sd, zeny, LOG_TYPE_PICKDROP_MONSTER) (inventory.rs) bound.
/// `penalty` is pc_level_penalty_mod(sd, PENALTY_EXP, nullptr, md), which comes with the monsters.
pub fn party_exp_share(c: &Ctx, party_id: u32, src: &Unit, base_exp: u64, job_exp: u64, zeny: i32, penalty: &mut dyn FnMut(u32) -> i32) {
    exp_share(c, party_id, src.map, src.kind == crate::map::BL_MOB, base_exp, job_exp, zeny, penalty, &mut |e| {
        crate::status::progress::pc_gainexp(c, e.aid, Some(src), e.base_exp, e.job_exp, 0);
        if e.zeny != 0 {
            // zeny from mobs [Valaris]
            crate::inventory::pc_getzeny(c, e.aid, e.zeny as i64, crate::map_log::LOG_TYPE_PICKDROP_MONSTER, 0);
        }
    });
}

/// pc.hpp pc_isidle_party (idle_no_share is off, so exp_share never asks).
fn pc_isidle_party(c: &Ctx, aid: u32) -> bool {
    let Some(p) = pc::get(c, aid) else { return false };
    let _ = p; // DIFF_TICK(last_tick, idletime) >= idle_no_share (seconds): with the idle system (phase 6)
    false
}

/// party.cpp party_share_loot: who gets an item a party member picked up. `additem(aid)` must run
/// pc_additem(target, item, amount, LOG_TYPE_PICKDROP_PLAYER) and return its result (0 = added). Returns
/// (pc_additem's failure for the picker, or 0; the member who got it — for clif_party_show_picker when
/// show_party_share_picker and show_picker.item_type match the item).
pub fn share_loot(c: &Ctx, party_id: u32, sd: u32, first_charid: u32, additem: &mut dyn FnMut(u32) -> i32) -> (i32, Option<u32>) {
    let p = party_search(c, party_id);
    let sd_map = unit::get(c, sd).map(|u| u.map);
    let mut target: Option<u32> = None;
    if let Some(mut p) = p.clone() {
        if p.party.item & 2 != 0 && (first_charid != 0 || party_share_type() & 1 == 0) {
            // item distribution to party members.
            if party_share_type() & 2 != 0 {
                // Round Robin
                let mut i = p.itemc as usize;
                loop {
                    i += 1;
                    if i >= MAX_PARTY {
                        i = 0; // reset counter to 1st person in party so it'll stop when it reaches "itemc"
                    }
                    let psd = p.data[i].sd;
                    let ok = psd != 0
                        && unit::get(c, psd).map(|u| Some(u.map) == sd_map).unwrap_or(false)
                        && !pc::isdead(c, psd)
                        && !(idle_no_share() && pc_isidle_party(c, psd));
                    if ok && additem(psd) == 0 {
                        // Successful pick.
                        p.itemc = i as u8;
                        put(c, &p);
                        target = Some(psd);
                        break;
                    }
                    if i == p.itemc as usize {
                        break;
                    }
                }
            } else {
                // Random pick: collect pick candidates
                let mut psd: Vec<u32> = Vec::new();
                for d in p.data.iter() {
                    if d.sd == 0 {
                        continue;
                    }
                    let same = unit::get(c, d.sd).map(|u| Some(u.map) == sd_map).unwrap_or(false);
                    if !same || pc::isdead(c, d.sd) || (idle_no_share() && pc_isidle_party(c, d.sd)) {
                        continue;
                    }
                    psd.push(d.sd);
                }
                let mut count = psd.len();
                while count > 0 {
                    // Pick a random member.
                    let i = c.rnd_value(0, count as i32 - 1) as usize;
                    if additem(psd[i]) != 0 {
                        // Discard this receiver.
                        psd[i] = psd[count - 1];
                        count -= 1;
                    } else {
                        target = Some(psd[i]);
                        break;
                    }
                }
            }
        }
    }
    if target.is_none() {
        target = Some(sd); // Give it to the char that picked it up
        let r = additem(sd);
        if r != 0 {
            return (r, None);
        }
    }
    (0, if p.is_some() && party_show_share_picker() { target } else { None })
}

/// party.cpp party_send_dot_remove.
pub fn party_send_dot_remove(c: &Ctx, sd: &Unit) {
    if party_id_of(c, sd.id) != 0 {
        clif_party_xy_remove(c, sd);
    }
}

/// party.cpp party_foreachsamemap's selection: the online members on sd's map (and within `range`, 0 = whole map)
/// who are in the block list, in member order.
pub fn party_foreachsamemap(c: &Ctx, sd: &Unit, range: i16) -> Vec<Unit> {
    let Some(p) = party_search(c, party_id_of(c, sd.id)) else { return Vec::new() };
    let (x0, y0, x1, y1) = (sd.x - range, sd.y - range, sd.x + range, sd.y + range);
    let mut out = Vec::new();
    for d in p.data.iter() {
        if d.sd == 0 {
            continue;
        }
        let Some(psd) = unit::get(c, d.sd) else { continue };
        if psd.map != sd.map || !psd.on_map {
            continue;
        }
        if range != 0 && (psd.x < x0 || psd.y < y0 || psd.x > x1 || psd.y > y1) {
            continue;
        }
        out.push(psd);
    }
    out
}

/// party.cpp party_sub_count: 1 when the member is neither autotrading nor idle (with idle_no_share).
pub fn party_sub_count(c: &Ctx, aid: u32) -> i32 {
    if crate::autotrade::autotrade(c, aid) != 0 {
        return 0;
    }
    if idle_no_share() && pc_isidle_party(c, aid) {
        return 0;
    }
    1
}

// ---------------------------------------------------------------------------------------------- party booking (0x0802…)

/// party.cpp party_booking_register.
pub fn party_booking_register(c: &Ctx, aid: u32, level: i16, mapid: i16, job: &[i16]) {
    let Some(sd) = unit::get(c, aid) else { return };
    if c.db().party_booking().char_id().find(sd.char_id).is_some() {
        // already registered
        clif_party_booking_register_ack(c, &sd, 2);
        return;
    }
    let mut s = state(c);
    let index = s.booking_nextid;
    s.booking_nextid = s.booking_nextid.wrapping_add(1);
    put_state(c, s);
    let jobs: Vec<i16> = (0..MAX_PARTY_BOOKING_JOBS).map(|i| if job[i] != 0xFF { job[i] } else { -1 }).collect();
    let charname = pc::character_of(c, aid).map(|ch| ch.name).unwrap_or_default();
    let starttime = (c.tick() / 1000) as i32;
    let pb = PartyBooking { char_id: sd.char_id, index, charname, starttime, level, mapid, job: jobs };
    c.db().party_booking().insert(pb.clone());
    clif_party_booking_register_ack(c, &sd, 0);
    clif_party_booking_insert_notify(c, &sd, &pb); // Notice
}

/// party.cpp party_booking_update.
pub fn party_booking_update(c: &Ctx, aid: u32, job: &[i16]) {
    let Some(sd) = unit::get(c, aid) else { return };
    let Some(mut pb) = c.db().party_booking().char_id().find(sd.char_id) else { return };
    pb.starttime = (c.tick() / 1000) as i32; // Update time.
    pb.job = (0..MAX_PARTY_BOOKING_JOBS).map(|i| if job[i] != 0xFF { job[i] } else { -1 }).collect();
    c.db().party_booking().char_id().update(pb.clone());
    clif_party_booking_update_notify(c, &sd, &pb);
}

/// party.cpp party_booking_search.
pub fn party_booking_search(c: &Ctx, aid: u32, level: i16, mapid: i16, job: i16, lastindex: u32, _resultcount: i16) {
    let Some(sd) = unit::get(c, aid) else { return };
    let mut all: Vec<PartyBooking> = c.db().party_booking().iter().collect();
    all.sort_by_key(|b| idb_order(b.char_id));
    let mut result: Vec<PartyBooking> = Vec::new();
    let mut more_result = false;
    for pb in all {
        if pb.index < lastindex || (level != 0 && (pb.level < level - 15 || pb.level > level)) {
            continue;
        }
        if result.len() >= MAX_PARTY_BOOKING_RESULTS {
            more_result = true;
            break;
        }
        let hit = if mapid == 0 && job == -1 {
            true
        } else if mapid == 0 {
            pb.job.iter().any(|&j| j == job && job != -1)
        } else if job == -1 {
            pb.mapid == mapid
        } else {
            false
        };
        if hit {
            result.push(pb);
        }
    }
    clif_party_booking_search_ack(c, &sd, &result, more_result);
}

/// party.cpp party_booking_delete (map_quit and CZ_PARTY_BOOKING_REQ_DELETE).
pub fn party_booking_delete(c: &Ctx, aid: u32) -> bool {
    let Some(ch) = pc::character_of(c, aid) else { return true };
    if let Some(pb) = c.db().party_booking().char_id().find(ch.id) {
        if let Some(sd) = unit::get(c, aid) {
            clif_party_booking_delete_notify(c, &sd, pb.index);
        }
        c.db().party_booking().char_id().delete(ch.id);
    }
    true
}

// ---------------------------------------------------------------------------------------------- clif_send PARTY*

/// The PARTY* targets of clif.cpp clif_send (plus SELF for the functions that take a target).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum PTarget {
    Party,
    PartyWos,
    PartySamemap,
    PartySamemapWos,
    PartyArea,
    PartyAreaWos,
    SelfOnly,
}

/// clif.cpp clif_send for the PARTY targets: `bl` is the source player; its party's online members receive.
pub fn send(c: &Ctx, buf: &[u8], bl: &Unit, target: PTarget) {
    if target == PTarget::SelfOnly {
        clif::send(c, buf, bl, Target::SelfOnly);
        return;
    }
    let pid = if bl.kind == crate::map::BL_PC { party_id_of(c, bl.id) } else { 0 };
    let Some(p) = party_search(c, pid) else { return };
    let r = crate::conf::area_size();
    let (x0, y0, x1, y1) = (bl.x - r, bl.y - r, bl.x + r, bl.y + r);
    for d in p.data.iter() {
        if d.sd == 0 {
            continue;
        }
        let Some(pcd) = pc::get(c, d.sd) else { continue };
        if pcd.disconnected {
            continue;
        }
        let Some(sd) = unit::get(c, d.sd) else { continue };
        if sd.id == bl.id && matches!(target, PTarget::PartyWos | PTarget::PartySamemapWos | PTarget::PartyAreaWos) {
            continue;
        }
        if target != PTarget::Party && target != PTarget::PartyWos && bl.map != sd.map {
            continue;
        }
        if matches!(target, PTarget::PartyArea | PTarget::PartyAreaWos) && (sd.x < x0 || sd.y < y0 || sd.x > x1 || sd.y > y1) {
            continue;
        }
        c.send(pcd.identity, buf);
    }
    // enable_spy (@partyspy) is off until a GM uses it (@partyspy is not ported)
}

// ---------------------------------------------------------------------------------------------- clif.cpp senders

/// common/mapindex.cpp mapindex_getmapname_ext: an instance map name "<id>#<map>" keeps the part after the '#'
/// (`sscanf(string, "%*[^#]%*[#]%15s", buf)`), at most MAP_NAME_LENGTH - 1 (11) characters are kept (a longer name is
/// cut, with rAthena's warning), and ".gat" is added unless the name already ends with it (case-insensitively): an
/// empty name gives ".gat".
fn map_ext(name: &str) -> String {
    const MAP_NAME_LENGTH: usize = 12;
    let is_space = |c: u8| matches!(c, b' ' | b'\t' | b'\n' | 0x0B | 0x0C | b'\r'); // C isspace
    let b = name.as_bytes();
    let mut buf: Vec<u8> = b.to_vec(); // strcpy(buf, string)
    let mut p = 0;
    while p < b.len() && b[p] != b'#' {
        p += 1;
    }
    // %*[^#] needs one character at least, %*[#] one '#' at least, %15s one non-blank at least
    if p > 0 && p < b.len() {
        let mut q = p;
        while q < b.len() && b[q] == b'#' {
            q += 1;
        }
        while q < b.len() && is_space(b[q]) {
            q += 1;
        }
        let start = q;
        while q < b.len() && q - start < 15 && !is_space(b[q]) {
            q += 1;
        }
        if q > start {
            buf = b[start..q].to_vec();
        }
    }
    let mut len = buf.len().min(MAP_NAME_LENGTH); // safestrnlen(buf, MAP_NAME_LENGTH)
    if len == MAP_NAME_LENGTH {
        log::warn!("(mapindex_normalize_name) Map name '{}' is too long!", String::from_utf8_lossy(&buf));
        len -= 1;
    }
    let mut dest = buf[..len].to_vec();
    if len < 4 || !dest[len - 4..].eq_ignore_ascii_case(b".gat") {
        dest.extend_from_slice(b".gat"); // add .gat extension
    }
    String::from_utf8_lossy(&dest).into_owned()
}

#[cfg(test)]
mod map_ext_tests {
    /// mapindex.cpp:45-70 on the edge cases the audit named (social W7).
    #[test]
    fn mapindex_getmapname_ext_cases() {
        assert_eq!(super::map_ext("prontera"), "prontera.gat");
        assert_eq!(super::map_ext(""), ".gat");
        assert_eq!(super::map_ext("izlude.gat"), "izlude.gat");
        assert_eq!(super::map_ext("IZLUDE.GAT"), "IZLUDE.GAT");
        // 12 characters are cut to 11 before the test for ".gat": rAthena warns and adds a second extension
        assert_eq!(super::map_ext("prontera.gat"), "prontera.ga.gat");
        assert_eq!(super::map_ext("1@tower"), "1@tower.gat");
        assert_eq!(super::map_ext("0001#1@tower"), "1@tower.gat");
        assert_eq!(super::map_ext("#1@tower"), "#1@tower.gat"); // %*[^#] matches nothing: buf keeps the whole name
        assert_eq!(super::map_ext("abcdefghijklmn"), "abcdefghijk.gat"); // 11 characters kept
    }
}

/// clif.cpp clif_party_created: ZC_ACK_MAKE_GROUP.
pub fn clif_party_created(c: &Ctx, sd: &Unit, result: i32) {
    let p = PACKET_ZC_ACK_MAKE_GROUP { PacketType: PACKET_ZC_ACK_MAKE_GROUP::ID as i16, result: result as i8 };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
}

/// packets_struct.hpp partymemberinfo / partyinfo for PACKETVER >= 20171207.
pub const HEADER_PARTYMEMBERINFO: u16 = 0x0ae4;
pub const HEADER_PARTYINFO: u16 = 0x0ae5;

/// clif.cpp clif_party_member_info: ZC_ADD_MEMBER_TO_GROUP (0x0ae4) about `aid`, to the party.
pub fn clif_party_member_info(c: &Ctx, party: &PartyData, aid: u32) {
    let Some(sd) = unit::get(c, aid) else { return };
    let Some(id) = party_getmemberid(party, aid, sd.char_id) else { return };
    let Some(ch) = pc::character_of(c, aid) else { return };
    let m = &party.party.member[id];
    // mapindex_getmapname_ext(map_mapid2mapname(sd.m)): an instance map shows its source map
    let mapname = crate::instance_map::map_mapid2mapname(c, sd.map as i32);
    let p = PACKET_ZC_ADD_MEMBER_TO_GROUP {
        packetType: HEADER_PARTYMEMBERINFO as i16,
        AID: ch.account_id,
        GID: ch.id,
        leader: if m.leader { 0 } else { 1 },
        class_: ch.class as i16,
        baseLevel: ch.base_level as i16,
        x: sd.x,
        y: sd.y,
        offline: if m.online { 0 } else { 1 },
        partyName: party.party.name.clone(),
        playerName: ch.name.clone(),
        mapName: map_ext(&mapname),
        sharePickup: (party.party.item & 1 != 0) as i8,
        shareLoot: (party.party.item & 2 != 0) as i8,
    };
    send(c, &p.to_vec(), &sd, PTarget::Party);
}

/// clif.cpp clif_party_info: ZC_GROUP_LIST (0x0ae5) to `sd` alone, or (None) to the whole party.
pub fn clif_party_info(c: &Ctx, party: &PartyData, sd: Option<u32>) {
    let (src, target) = match sd {
        None => match party_getavailablesd(party) {
            Some(a) => (a, PTarget::Party), // Send to whole party
            None => return,                 // Nobody is online to notify
        },
        Some(a) => (a, PTarget::SelfOnly), // Send only to self
    };
    let Some(su) = unit::get(c, src) else { return };
    let mut members = Vec::new();
    for m in party.party.member.iter() {
        if m.account_id == 0 {
            continue;
        }
        members.push(PACKET_ZC_GROUP_LIST_SUB {
            AID: m.account_id,
            GID: m.char_id,
            playerName: m.name.clone(),
            mapName: map_ext(&m.map),
            leader: if m.leader { 0 } else { 1 },
            offline: if m.online { 0 } else { 1 },
            class_: m.class as i16,
            baseLevel: m.lv as i16,
        });
    }
    let p = PACKET_ZC_GROUP_LIST { packetType: HEADER_PARTYINFO as i16, packetLen: 0, partyName: party.party.name.clone(), members };
    send(c, &p.to_vec(), &su, target);
}

/// clif.cpp clif_partyinvitationstate: ZC_PARTY_CONFIG.
pub fn clif_partyinvitationstate(c: &Ctx, sd: &Unit) {
    let deny = pc::character_of(c, sd.id).map(|ch| ch.disable_partyinvite).unwrap_or(0);
    let p = PACKET_ZC_PARTY_CONFIG { packetType: PACKET_ZC_PARTY_CONFIG::ID as i16, denyPartyInvites: deny };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
}

/// clif.cpp clif_party_invite: ZC_PARTY_JOIN_REQ to the invited player.
pub fn clif_party_invite(c: &Ctx, sd: &Unit, tsd: &Unit) {
    let Some(party) = party_search(c, party_id_of(c, sd.id)) else { return };
    let p = PACKET_ZC_PARTY_JOIN_REQ { PacketType: PACKET_ZC_PARTY_JOIN_REQ::ID as i16, GRID: party.party_id as i32, groupName: party.party.name.clone() };
    clif::send(c, &p.to_vec(), tsd, Target::SelfOnly);
}

/// clif.cpp clif_party_invite_reply: ZC_PARTY_JOIN_REQ_ACK.
pub fn clif_party_invite_reply(c: &Ctx, sd: &Unit, nick: &str, reply: i32) {
    let p = PACKET_ZC_PARTY_JOIN_REQ_ACK { PacketType: PACKET_ZC_PARTY_JOIN_REQ_ACK::ID as i16, characterName: nick.to_string(), result: reply };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
}

/// clif.cpp clif_party_option: ZC_REQ_GROUPINFO_CHANGE_V2 (0x07d8) — flag 0 to the party, otherwise to `sd`;
/// flag & 1 shows the exp option as 2 ("cannot change").
pub fn clif_party_option(c: &Ctx, p: &PartyData, sd: Option<u32>, flag: i32) {
    let mut sd = sd;
    if sd.is_none() && flag == 0 {
        sd = party_getavailablesd(p);
    }
    let Some(aid) = sd else { return };
    let Some(su) = unit::get(c, aid) else { return };
    let mut w = Vec::with_capacity(8);
    w.extend_from_slice(&0x07d8u16.to_le_bytes());
    let exp: u32 = if flag & 0x01 != 0 { 2 } else { p.party.exp as u32 };
    w.extend_from_slice(&exp.to_le_bytes());
    w.push((p.party.item & 1 != 0) as u8);
    w.push((p.party.item & 2 != 0) as u8);
    if flag == 0 {
        send(c, &w, &su, PTarget::Party);
    } else {
        send(c, &w, &su, PTarget::SelfOnly);
    }
}

/// clif.cpp clif_party_withdraw: ZC_DELETE_MEMBER_FROM_GROUP.
pub fn clif_party_withdraw(c: &Ctx, sd: &Unit, account_id: u32, name: &str, result: u8, target: PTarget) {
    let p = PACKET_ZC_DELETE_MEMBER_FROM_GROUP { PacketType: PACKET_ZC_DELETE_MEMBER_FROM_GROUP::ID as i16, AID: account_id as i32, characterName: name.to_string(), result: result as i8 };
    send(c, &p.to_vec(), sd, target);
}

/// clif.cpp clif_party_message: ZC_NOTIFY_CHAT_PARTY to the party (`len` counts the NUL).
pub fn clif_party_message(c: &Ctx, party: &PartyData, account_id: u32, mes: &[u8], len: usize) {
    let Some(sd) = party_getavailablesd(party).and_then(|a| unit::get(c, a)) else { return };
    let mut len = len;
    if len > crate::net::CHAT_SIZE_MAX {
        log::warn!("clif_party_message: Truncated message (len={len}, max={}, party_id={}).", crate::net::CHAT_SIZE_MAX, party.party_id);
        len = crate::net::CHAT_SIZE_MAX;
    }
    // safestrncpy(chatMsg, mes, len): len - 1 characters and a NUL
    let mut m: Vec<u8> = mes.iter().copied().take_while(|&b| b != 0).take(len.saturating_sub(1)).collect();
    m.resize(len, 0);
    let p = PACKET_ZC_NOTIFY_CHAT_PARTY { PacketType: PACKET_ZC_NOTIFY_CHAT_PARTY::ID as i16, PacketLength: 0, AID: account_id as i32, chatMsg: m };
    send(c, &p.to_vec(), &sd, PTarget::Party);
}

/// clif.cpp clif_party_xy: ZC_NOTIFY_POSITION_TO_GROUPM to the members on the same map.
pub fn clif_party_xy(c: &Ctx, sd: &Unit) {
    let p = PACKET_ZC_NOTIFY_POSITION_TO_GROUPM { PacketType: PACKET_ZC_NOTIFY_POSITION_TO_GROUPM::ID as i16, AID: sd.id as i32, xPos: sd.x, yPos: sd.y };
    send(c, &p.to_vec(), sd, PTarget::PartySamemapWos);
}

/// clif.cpp clif_party_xy_single: `sd`'s position to `tsd`.
pub fn clif_party_xy_single(c: &Ctx, sd: &Unit, tsd: &Unit) {
    let p = PACKET_ZC_NOTIFY_POSITION_TO_GROUPM { PacketType: PACKET_ZC_NOTIFY_POSITION_TO_GROUPM::ID as i16, AID: sd.id as i32, xPos: sd.x, yPos: sd.y };
    clif::send(c, &p.to_vec(), tsd, Target::SelfOnly);
}

/// clif.cpp clif_party_hp: ZC_NOTIFY_HP_TO_GROUPM_R2 (0x080e) to the members around. Other systems call this when
/// the player's HP changes (clif_update_hp with party_hp_mode 0).
pub fn clif_party_hp(c: &Ctx, sd: &Unit) {
    // sd->battle_status.hp / max_hp (the status system's battle status)
    let (hp, maxhp) = crate::status::status_data_of(c, sd.id).map(|s| (s.hp as i32, s.max_hp as i32)).unwrap_or((sd.hp as i32, sd.max_hp as i32));
    let p = PACKET_ZC_NOTIFY_HP_TO_GROUPM { PacketType: PACKET_ZC_NOTIFY_HP_TO_GROUPM::ID as i16, AID: sd.id, hp, maxhp };
    send(c, &p.to_vec(), sd, PTarget::PartyAreaWos);
}

/// clif.cpp clif_update_hp's party part: `if (!battle_config.party_hp_mode && sd.status.party_id) clif_party_hp(sd)`
/// (status/clif.rs update_hp).
pub fn update_hp(c: &Ctx, sd: &Unit) {
    if party_hp_mode() == 0 && party_id_of(c, sd.id) != 0 {
        clif_party_hp(c, sd);
    }
}

/// clif.cpp clif_updatestatus's SP_BASELEVEL follow-up: the member's level in the party data, then
/// clif_party_job_and_level.
pub fn updatestatus_baselevel(c: &Ctx, sd: &Unit) {
    let pid = party_id_of(c, sd.id);
    if pid == 0 {
        return;
    }
    let Some(ch) = pc::character_of(c, sd.id) else { return };
    let Some(mut p) = party_search(c, pid) else { return };
    if let Some(i) = p.party.member.iter().position(|m| m.char_id == ch.id) {
        p.party.member[i].lv = ch.base_level;
        put(c, &p);
        clif_party_job_and_level(c, sd);
    }
}

/// clif.cpp clif_party_dead: ZC_GROUP_ISALIVE to the party (pc_dead / pc_revive call this).
pub fn clif_party_dead(c: &Ctx, sd: &Unit) {
    let p = PACKET_ZC_GROUP_ISALIVE { packetType: PACKET_ZC_GROUP_ISALIVE::ID as i16, AID: sd.id, isDead: pc::isdead(c, sd.id) as u8 };
    send(c, &p.to_vec(), sd, PTarget::Party);
}

/// clif.cpp clif_party_job_and_level: ZC_NOTIFY_MEMBERINFO_TO_GROUPM to the party (job change / level up).
pub fn clif_party_job_and_level(c: &Ctx, sd: &Unit) {
    let Some(ch) = pc::character_of(c, sd.id) else { return };
    let p = PACKET_ZC_NOTIFY_MEMBERINFO_TO_GROUPM { PacketType: PACKET_ZC_NOTIFY_MEMBERINFO_TO_GROUPM::ID as i16, AID: sd.id as i32, job: ch.class as i16, level: ch.base_level as i16 };
    send(c, &p.to_vec(), sd, PTarget::Party);
}


/// clif.cpp clif_party_xy_remove: the minimap dot goes (-1, -1).
pub fn clif_party_xy_remove(c: &Ctx, sd: &Unit) {
    let p = PACKET_ZC_NOTIFY_POSITION_TO_GROUPM { PacketType: PACKET_ZC_NOTIFY_POSITION_TO_GROUPM::ID as i16, AID: sd.id as i32, xPos: -1, yPos: -1 };
    send(c, &p.to_vec(), sd, PTarget::PartySamemapWos);
}

/// clif.cpp clif_party_leaderchanged: ZC_CHANGE_GROUP_MASTER (0x07fc) to the party.
pub fn clif_party_leaderchanged(c: &Ctx, sd: &Unit, prev_leader_aid: u32, new_leader_aid: u32) {
    let mut w = Vec::with_capacity(10);
    w.extend_from_slice(&0x07fcu16.to_le_bytes());
    w.extend_from_slice(&prev_leader_aid.to_le_bytes());
    w.extend_from_slice(&new_leader_aid.to_le_bytes());
    send(c, &w, sd, PTarget::Party);
}

/// clif.hpp e_clif_messages MSI_PARTY_MASTER_CHANGE_SAME_MAP (PACKETVER outside 20130807–20130814).
pub const MSI_PARTY_MASTER_CHANGE_SAME_MAP: u16 = 2094;


/// clif.cpp clif_PartyBookingRegisterAck: 0x0803.
fn clif_party_booking_register_ack(c: &Ctx, sd: &Unit, flag: i16) {
    let mut w = Vec::with_capacity(4);
    w.extend_from_slice(&0x0803u16.to_le_bytes());
    w.extend_from_slice(&flag.to_le_bytes());
    clif::send(c, &w, sd, Target::SelfOnly);
}

/// The 48-byte party_booking_ad_info as clif writes it (index, name, "expire" time, level, map, jobs).
fn booking_entry(w: &mut Vec<u8>, pb: &PartyBooking) {
    w.extend_from_slice(&pb.index.to_le_bytes());
    crate::net::wire::put_str(w, &pb.charname, NAME_LENGTH);
    w.extend_from_slice(&pb.starttime.to_le_bytes()); // FIXME: This is expire time
    w.extend_from_slice(&pb.level.to_le_bytes());
    w.extend_from_slice(&pb.mapid.to_le_bytes());
    for j in 0..MAX_PARTY_BOOKING_JOBS {
        w.extend_from_slice(&pb.job.get(j).copied().unwrap_or(-1).to_le_bytes());
    }
}

/// clif.cpp clif_PartyBookingSearchAck: 0x0805 (entries of sizeof(party_booking_ad_info) = 48 bytes). The size is
/// the struct's, and party.hpp:44-49 declares `unsigned long index` and `long starttime`: 48 bytes on Windows (where
/// rAthena's "(48)" comment and the client's layout come from), 56 on the 64-bit Linux oracle, whose entries then
/// carry 8 unwritten (uninitialised) bytes each at a 56-byte stride. The module keeps the 48-byte layout the client
/// reads; a parity run against the WSL oracle differs here by construction.
fn clif_party_booking_search_ack(c: &Ctx, sd: &Unit, results: &[PartyBooking], more_result: bool) {
    const SIZE: usize = 48;
    let mut w = Vec::with_capacity(5 + SIZE * results.len());
    w.extend_from_slice(&0x0805u16.to_le_bytes());
    w.extend_from_slice(&((SIZE * results.len() + 5) as u16).to_le_bytes());
    w.push(more_result as u8);
    for pb in results {
        let st = w.len();
        booking_entry(&mut w, pb);
        w.resize(st + SIZE, 0);
    }
    clif::send(c, &w, sd, Target::SelfOnly);
}

/// clif.cpp clif_PartyBookingDeleteAck: 0x0807.
fn clif_party_booking_delete_ack(c: &Ctx, sd: &Unit, flag: i16) {
    let mut w = Vec::with_capacity(4);
    w.extend_from_slice(&0x0807u16.to_le_bytes());
    w.extend_from_slice(&flag.to_le_bytes());
    clif::send(c, &w, sd, Target::SelfOnly);
}

/// clif.cpp clif_PartyBookingInsertNotify: 0x0809 to every client.
fn clif_party_booking_insert_notify(c: &Ctx, sd: &Unit, pb: &PartyBooking) {
    let mut w = Vec::with_capacity(38 + MAX_PARTY_BOOKING_JOBS * 2);
    w.extend_from_slice(&0x0809u16.to_le_bytes());
    booking_entry(&mut w, pb);
    clif::send(c, &w, sd, Target::AllClient);
}

/// clif.cpp clif_PartyBookingUpdateNotify: 0x080a to every client.
fn clif_party_booking_update_notify(c: &Ctx, sd: &Unit, pb: &PartyBooking) {
    let mut w = Vec::with_capacity(6 + MAX_PARTY_BOOKING_JOBS * 2);
    w.extend_from_slice(&0x080au16.to_le_bytes());
    w.extend_from_slice(&pb.index.to_le_bytes());
    for j in 0..MAX_PARTY_BOOKING_JOBS {
        w.extend_from_slice(&pb.job.get(j).copied().unwrap_or(-1).to_le_bytes());
    }
    clif::send(c, &w, sd, Target::AllClient);
}

/// clif.cpp clif_PartyBookingDeleteNotify: 0x080b to every client.
fn clif_party_booking_delete_notify(c: &Ctx, sd: &Unit, index: u32) {
    let mut w = Vec::with_capacity(6);
    w.extend_from_slice(&0x080bu16.to_le_bytes());
    w.extend_from_slice(&index.to_le_bytes());
    clif::send(c, &w, sd, Target::AllClient);
}

/// clif.cpp clif_partybooking_ask: ZC_PARTY_REQ_MASTER_TO_JOIN to the leader.
fn clif_partybooking_ask(c: &Ctx, sd: &Unit, joining: u32) {
    let Some(ch) = pc::character_of(c, joining) else { return };
    let p = PACKET_ZC_PARTY_REQ_MASTER_TO_JOIN { packetType: PACKET_ZC_PARTY_REQ_MASTER_TO_JOIN::ID as i16, CID: ch.id, AID: ch.account_id, name: ch.name.clone(), x: ch.base_level, y: ch.class };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
}

/// clif.cpp clif_partybooking_reply: ZC_PARTY_JOIN_REQ_ACK_FROM_MASTER to the applicant.
fn clif_partybooking_reply(c: &Ctx, sd: &Unit, leader: u32, accepted: bool) {
    let pid = party_id_of(c, leader);
    if pid == 0 {
        return;
    }
    let Some(party) = party_search(c, pid) else { return };
    let lname = pc::character_of(c, leader).map(|ch| ch.name).unwrap_or_default();
    let p = PACKET_ZC_PARTY_JOIN_REQ_ACK_FROM_MASTER {
        packetType: PACKET_ZC_PARTY_JOIN_REQ_ACK_FROM_MASTER::ID as i16,
        player_name: lname,
        party_name: party.party.name.clone(),
        AID: leader,
        refused: (!accepted) as u32,
    };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
}

// ---------------------------------------------------------------------------------------------- clif.cpp parsers

fn pos_reader<'a>(pkt: &'a [u8], i: usize) -> Reader<'a> {
    let cmd = u16::from_le_bytes([pkt[0], pkt[1]]);
    let pos = packet_pos(cmd);
    Reader::at(pkt, pos.get(i).copied().unwrap_or(2) as usize)
}

/// The basic-skill check party creation shares (NV_BASIC 7 or SU_BASIC_SKILL).
fn can_create(c: &Ctx, sd: &Unit) -> bool {
    if crate::npc::mapflag(c, sd.map, crate::npc::MF_PARTYLOCK) {
        // Party locked.
        clif::displaymessage(c, sd, crate::data::msg_txt(227));
        return false;
    }
    if crate::conf::basic_skill_check() && crate::skill::checkskill(c, sd.id, 1) < 7 && crate::skill::checkskill(c, sd.id, 5018) < 1 {
        crate::skill::skill_fail(c, sd, 1, 4);
        return false;
    }
    true
}

/// clif.cpp clif_parse_CreateParty (CZ_MAKE_GROUP 0x00f9).
pub fn parse_create_party(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(sd) = unit::get(c, aid) else { return };
    if !can_create(c, &sd) {
        return;
    }
    let name: Vec<u8> = pkt.get(2..).unwrap_or(&[]).iter().copied().take(NAME_LENGTH).take_while(|&b| b != 0).take(NAME_LENGTH - 1).collect();
    party_create(c, aid, &name, 0, 0);
}

/// clif.cpp clif_parse_CreateParty2 (CZ_MAKE_GROUP2 0x01e8).
pub fn parse_create_party2(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(sd) = unit::get(c, aid) else { return };
    if !can_create(c, &sd) {
        return;
    }
    let Some(p) = PACKET_CZ_MAKE_GROUP2::read(&mut Reader::new(pkt)) else { return };
    let name: Vec<u8> = latin1(&p.name).into_iter().take(NAME_LENGTH - 1).collect();
    party_create(c, aid, &name, p.item_pickup as i32, p.item_share as i32);
}

/// clif.cpp clif_parse_PartyInvite (CZ_REQ_JOIN_GROUP 0x00fc: by account id).
pub fn parse_party_invite(c: &Ctx, aid: u32, pkt: &[u8]) {
    let target = Reader::at(pkt, 2).u32().unwrap_or(0);
    let tsd = pc::get(c, target).map(|_| target);
    party_invite(c, aid, tsd);
}

/// clif.cpp clif_parse_PartyInvite2 (CZ_PARTY_JOIN_REQ 0x02c4: by name).
pub fn parse_party_invite2(c: &Ctx, aid: u32, pkt: &[u8]) {
    let name = pos_reader(pkt, 0).string(NAME_LENGTH).unwrap_or_default();
    let name: String = from_latin1(&latin1(&name).into_iter().take(NAME_LENGTH - 1).collect::<Vec<u8>>());
    let tsd = pc::nick2sd(c, &name, false).map(|u| u.id);
    party_invite(c, aid, tsd);
}

/// clif.cpp clif_parse_ReplyPartyInvite (CZ_JOIN_GROUP 0x00ff: party id, flag.L).
pub fn parse_reply_party_invite(c: &Ctx, aid: u32, pkt: &[u8]) {
    let party_id = Reader::at(pkt, 2).u32().unwrap_or(0);
    let flag = Reader::at(pkt, 6).i32().unwrap_or(0);
    party_reply_invite(c, aid, party_id, flag);
}

/// clif.cpp clif_parse_ReplyPartyInvite2 (CZ_PARTY_JOIN_REQ_ACK 0x02c7: party id, flag.B).
pub fn parse_reply_party_invite2(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_PARTY_JOIN_REQ_ACK::read(&mut Reader::new(pkt)) else { return };
    party_reply_invite(c, aid, p.party_id, p.flag as i32);
}

/// clif.cpp clif_parse_LeaveParty (CZ_REQ_LEAVE_GROUP 0x0100).
pub fn parse_leave_party(c: &Ctx, aid: u32) {
    party_leave(c, aid, true);
}

/// clif.cpp clif_parse_RemovePartyMember (CZ_REQ_EXPEL_GROUP_MEMBER 0x0103).
pub fn parse_remove_party_member(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_REQ_EXPEL_GROUP_MEMBER::read(&mut Reader::new(pkt)) else { return };
    party_removemember(c, aid, p.AID, &p.name);
}

/// clif.cpp clif_parse_PartyChangeOption (0x0102 exp only / CZ_GROUPINFO_CHANGE_V2 0x07d7).
pub fn parse_party_change_option(c: &Ctx, aid: u32, pkt: &[u8]) {
    let pid = party_id_of(c, aid);
    if pid == 0 {
        return;
    }
    let Some(p) = party_search(c, pid) else { return };
    let Some(i) = p.data.iter().position(|d| d.sd == aid) else { return }; // Shouldn't happen
    if !p.party.member[i].leader {
        return;
    }
    let cmd = u16::from_le_bytes([pkt[0], pkt[1]]);
    let expflag = pos_reader(pkt, 0).i32().unwrap_or(0);
    if cmd == 0x0102 {
        // Client can't change the item-field
        party_changeoption(c, aid, expflag, p.party.item as i32);
    } else {
        let a = pos_reader(pkt, 1).u8().unwrap_or(0);
        let b = pos_reader(pkt, 2).u8().unwrap_or(0);
        let itemflag = (a != 0) as i32 | (((b != 0) as i32) << 1);
        party_changeoption(c, aid, expflag, itemflag);
    }
}

/// clif.cpp clif_parse_PartyMessage (CZ_REQUEST_CHAT_PARTY 0x0108).
pub fn parse_party_message(c: &Ctx, aid: u32, pkt: &[u8]) {
    // validate packet and retrieve name and message
    let Some((_name, _message, output)) = crate::parse::process_message(c, aid, pkt, false) else { return };
    let b = latin1(&output);
    party_send_message(c, aid, &b, b.len() + 1);
}

/// clif.cpp clif_parse_PartyChangeLeader (CZ_CHANGE_GROUP_MASTER 0x07da).
pub fn parse_party_change_leader(c: &Ctx, aid: u32, pkt: &[u8]) {
    let t = pos_reader(pkt, 0).u32().unwrap_or(0);
    let tsd = pc::get(c, t).map(|_| t);
    party_changeleader(c, Some(aid), tsd, None);
}

/// clif.cpp clif_parse_PartyBookingRegisterReq (0x0802: level, map id, jobs as bytes of the 6 words).
pub fn parse_party_booking_register_req(c: &Ctx, aid: u32, pkt: &[u8]) {
    let level = pos_reader(pkt, 0).i16().unwrap_or(0);
    let mapid = pos_reader(pkt, 1).i16().unwrap_or(0);
    let cmd = u16::from_le_bytes([pkt[0], pkt[1]]);
    let idx = packet_pos(cmd).get(2).copied().unwrap_or(6) as usize;
    // RFIFOB(fd, idxpbj + i*2): the low byte of each job word
    let job: Vec<i16> = (0..MAX_PARTY_BOOKING_JOBS).map(|i| pkt.get(idx + i * 2).copied().unwrap_or(0) as i16).collect();
    party_booking_register(c, aid, level, mapid, &job);
}

/// clif.cpp clif_parse_PartyBookingSearchReq (0x0804).
pub fn parse_party_booking_search_req(c: &Ctx, aid: u32, pkt: &[u8]) {
    let level = pos_reader(pkt, 0).i16().unwrap_or(0);
    let mapid = pos_reader(pkt, 1).i16().unwrap_or(0);
    let job = pos_reader(pkt, 2).i16().unwrap_or(0);
    let lastindex = pos_reader(pkt, 3).u32().unwrap_or(0);
    let resultcount = pos_reader(pkt, 4).i16().unwrap_or(0);
    party_booking_search(c, aid, level, mapid, job, lastindex, resultcount);
}

/// clif.cpp clif_parse_PartyBookingDeleteReq (0x0806).
pub fn parse_party_booking_delete_req(c: &Ctx, aid: u32) {
    if party_booking_delete(c, aid) {
        if let Some(sd) = unit::get(c, aid) {
            clif_party_booking_delete_ack(c, &sd, 0);
        }
    }
}

/// clif.cpp clif_parse_PartyBookingUpdateReq (0x0808: 6 job words).
pub fn parse_party_booking_update_req(c: &Ctx, aid: u32, pkt: &[u8]) {
    let cmd = u16::from_le_bytes([pkt[0], pkt[1]]);
    let idx = packet_pos(cmd).first().copied().unwrap_or(2) as usize;
    let job: Vec<i16> = (0..MAX_PARTY_BOOKING_JOBS).map(|i| Reader::at(pkt, idx + i * 2).i16().unwrap_or(0)).collect();
    party_booking_update(c, aid, &job);
}

/// clif.cpp clif_parse_PartyTick (CZ_PARTY_CONFIG 0x02c8: /accept, /refuse).
pub fn parse_party_tick(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_PARTY_CONFIG::read(&mut Reader::new(pkt)) else { return };
    if let Some(mut ch) = pc::character_of(c, aid) {
        ch.disable_partyinvite = p.refuseInvite;
        c.db().character().id().update(ch);
    }
    if let Some(sd) = unit::get(c, aid) {
        clif_partyinvitationstate(c, &sd);
    }
}

/// clif.cpp clif_parse_partybooking_join (CZ_PARTY_REQ_MASTER_TO_JOIN 0x0ae6).
pub fn parse_partybooking_join(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_PARTY_REQ_MASTER_TO_JOIN::read(&mut Reader::new(pkt)) else { return };
    // Character is already in a party
    if party_id_of(c, aid) != 0 {
        return;
    }
    // Target player is offline
    let Some(tsd) = pc::charid2sd(c, p.CID) else { return };
    if tsd.id != p.AID {
        return;
    }
    let Some((min, max)) = party_booking_load(c, tsd.id, tsd.char_id) else { return };
    let lv = pc::character_of(c, aid).map(|ch| ch.base_level).unwrap_or(0);
    if lv < min || lv > max {
        return;
    }
    let my_cid = pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
    let mut tx = social::sx(c, tsd.id);
    // Already requested to join this party
    if tx.party_booking_requests.contains(&my_cid) {
        return;
    }
    // Store information that the player tried to join the party
    tx.party_booking_requests.push(my_cid);
    social::put_sx(c, &tx);
    clif_partybooking_ask(c, &tsd, aid);
}

/// clif.cpp clif_parse_partybooking_reply (CZ_PARTY_REQ_ACK_MASTER_TO_JOIN 0x0af8).
pub fn parse_partybooking_reply(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_PARTY_REQ_ACK_MASTER_TO_JOIN::read(&mut Reader::new(pkt)) else { return };
    // Target player is offline
    let Some(tsd) = pc::charid2sd(c, p.CID) else { return };
    if tsd.id != p.AID {
        return;
    }
    let mut x: PcSocial = social::sx(c, aid);
    // Check if the player even requested to join the party
    let Some(pos) = x.party_booking_requests.iter().position(|&cid| cid == p.CID) else { return };
    x.party_booking_requests.remove(pos);
    social::put_sx(c, &x);
    // Only party leaders can reply
    if !party_isleader(c, aid) {
        return;
    }
    let Some(party) = party_search(c, party_id_of(c, aid)) else { return };
    if party.instance_id > 0 && instance_block_invite() {
        return;
    }
    if p.accept != 0 {
        party_join(c, tsd.id, party.party_id);
    }
    clif_partybooking_reply(c, &tsd, aid, p.accept != 0);
}

// ---------------------------------------------------------------------------------------------- script helpers

/// mapreg_setreg(reference_uid(add_str(name), idx), v) for the engine's global `$@` variables.
pub fn mapreg_setreg(c: &Ctx, name: &str, idx: u32, v: i64) {
    use crate::script::vm::*;
    let rt = Rt::new(c);
    let mut st = State::new(CodeKey::Func(String::new()), std::rc::Rc::new(Default::default()), 0, 0, 0);
    crate::script::reg::store_set_num(&rt, &mut st, &crate::script::reg::Store::Map, &name.to_ascii_lowercase(), idx, v);
}

/// mapreg_setregstr for `$@…$` variables.
pub fn mapreg_setregstr(c: &Ctx, name: &str, idx: u32, v: &[u8]) {
    use crate::script::vm::*;
    let rt = Rt::new(c);
    let mut st = State::new(CodeKey::Func(String::new()), std::rc::Rc::new(Default::default()), 0, 0, 0);
    crate::script::reg::store_set_str(&rt, &mut st, &crate::script::reg::Store::Map, &name.to_ascii_lowercase(), idx, v);
}

/// party_create_byscript = 1 (the script party_create command).
pub fn set_create_byscript(c: &Ctx) {
    let mut s = state(c);
    s.create_byscript = true;
    put_state(c, s);
}
