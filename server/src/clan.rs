//! Clans: map/clan.cpp (members online, join / leave, clan chat), char/int_clan.cpp (the clans the char server loads
//! from its `clan` / `clan_alliance` tables — sql-files/main.sql's four default clans, embedded by
//! tools/datagen_mods/clans.py), the map/intif.cpp clan messages, clif.cpp's clan packets (ZC_CLANINFO 0x098a,
//! ZC_NOTIFY_CLAN_CONNECTINFO 0x0988, ZC_ACK_CLAN_LEAVE 0x0989, ZC_NOTIFY_CLAN_CHAT 0x098e, CZ_CLAN_CHAT 0x098d) and
//! the CLAN send target, char.cpp's char_clan_member_cleanup, the script commands clan_join / clan_leave
//! (buildins/extras.rs) and @clanspy (atcommand_extras.rs).
//!
//! State: the clans are static data (no script or command creates one); `clan_state` is the map server's runtime
//! part of struct clan (members[] — the online members by slot —, connect_member, instance_id); `pc_clan` is
//! sd->clan (the clan a character's session is attached to). The character's clan_id is its `character` row.
//! With one map server intif_clan_member_joined / _left / intif_clan_message send nothing (other_mapserver_count < 1),
//! so the char server's online counts never change and nothing comes back.

use spacetimedb::{table, Table};

use crate::world::Ctx;

/// mmo.hpp MAX_CLAN, MAX_CLANALLIANCE, NAME_LENGTH, MAP_NAME_LENGTH.
pub const MAX_CLAN: usize = 500;
pub const MAX_CLANALLIANCE: usize = 6;
const NAME_LENGTH: usize = 24;
const MAP_NAME_LENGTH: usize = 12;

/// conf/battle/instance.conf `instance_block_invite: yes`, `instance_block_leave: yes`.
fn instance_block_invite() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::INSTANCE_BLOCK_INVITE) != 0
}
fn instance_block_leave() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::INSTANCE_BLOCK_LEAVE) != 0
}
/// conf/map_athena.conf `enable_spy: no`.
pub const ENABLE_SPY: bool = false;
/// conf/char_athena.conf `clan_remove_inactive_days: 14`.
const CLAN_REMOVE_INACTIVE_DAYS: i64 = 14;
/// char.cpp do_init: add_timer_interval(gettick() + 1000, char_clan_member_cleanup, 0, 0, 60 * 60 * 1000).
const CLAN_CLEANUP_INTERVAL: i64 = 60 * 60 * 1000;

// ------------------------------------------------------------------------------------------------ data

/// mmo.hpp struct clan_alliance.
#[derive(serde::Deserialize, Clone, Debug, Default)]
pub struct ClanAlliance {
    pub opposition: i32,
    pub clan_id: i32,
    pub name: String,
}

/// mmo.hpp struct clan (the fields the char server loads).
#[derive(serde::Deserialize, Clone, Debug, Default)]
pub struct ClanData {
    pub id: i32,
    pub name: String,
    pub master: String,
    pub map: String,
    pub max_member: i16,
    pub alliances: Vec<ClanAlliance>,
}

#[derive(serde::Deserialize)]
pub struct ClanDb {
    pub clans: Vec<ClanData>,
}

crate::dataset!(clan_db, "misc_clan", ClanDb);

/// clan.cpp clan_search (the map server's clan_db).
pub fn clan_search(id: i32) -> Option<&'static ClanData> {
    clan_db().clans.iter().find(|c| c.id == id)
}

/// clan.cpp clan_searchname: strncmpi over NAME_LENGTH. (clan_db is an idb: its iteration is bucket order, which
/// for the ids 1-4 is id order.)
pub fn clan_searchname(name: &str) -> Option<&'static ClanData> {
    let n = crate::net::wire::latin1(name);
    let n = &n[..n.len().min(NAME_LENGTH)];
    let mut v: Vec<&ClanData> = clan_db().clans.iter().collect();
    v.sort_by_key(|c| c.id);
    v.into_iter().find(|c| {
        let cn = crate::net::wire::latin1(&c.name);
        let cn = &cn[..cn.len().min(NAME_LENGTH)];
        cn.eq_ignore_ascii_case(n)
    })
}

// ------------------------------------------------------------------------------------------------ tables

/// The map server's runtime part of a clan.
#[table(accessor = clan_state)]
#[derive(Clone, Debug)]
pub struct ClanState {
    #[primary_key]
    pub id: i32,
    /// connect_member.
    pub connect_member: i16,
    /// members[MAX_CLAN]: account ids by slot (0 = empty; slots past the end are empty).
    pub members: Vec<u32>,
    /// struct clan's `uint16 instance_id` (mmo.hpp): the clan's instance (instance.rs).
    pub instance_id: u16,
}

/// sd->clan: the clan the session is attached to.
#[table(accessor = pc_clan)]
#[derive(Clone, Debug)]
pub struct PcClan {
    #[primary_key]
    pub account_id: u32,
    pub clan_id: i32,
}

fn state(c: &Ctx, id: i32) -> ClanState {
    c.db().clan_state().id().find(id).unwrap_or(ClanState { id, connect_member: 0, members: Vec::new(), instance_id: 0 })
}

fn put_state(c: &Ctx, s: &ClanState) {
    if c.db().clan_state().id().find(s.id).is_some() {
        c.db().clan_state().id().update(s.clone());
    } else {
        c.db().clan_state().insert(s.clone());
    }
}

/// clan->instance_id (instance.rs: instance_create / instance_destroy / script_instancegetid).
pub fn instance_id(c: &Ctx, clan_id: i32) -> u16 {
    c.db().clan_state().id().find(clan_id).map(|s| s.instance_id).unwrap_or(0)
}

/// clan->instance_id = v (a uint16: rAthena stores the int32 instance id in it).
pub fn set_instance_id(c: &Ctx, clan_id: i32, v: u16) {
    let mut s = state(c, clan_id);
    s.instance_id = v;
    put_state(c, &s);
}

/// clan->members[0 .. MAX_CLAN): the online members' account ids by slot (0 = empty), the slots past the clan's
/// max_member included as empty (instance_check_clan walks MAX_CLAN of them).
pub fn member_slots(c: &Ctx, clan_id: i32) -> Vec<u32> {
    let mut v = state(c, clan_id).members;
    v.resize(MAX_CLAN, 0);
    v
}

/// sd->clan (its clan id), None when the session has no clan.
pub fn sd_clan(c: &Ctx, aid: u32) -> Option<i32> {
    c.db().pc_clan().account_id().find(aid).map(|p| p.clan_id)
}

fn set_sd_clan(c: &Ctx, aid: u32, clan: Option<i32>) {
    match clan {
        Some(clan_id) => {
            if c.db().pc_clan().account_id().find(aid).is_some() {
                c.db().pc_clan().account_id().update(PcClan { account_id: aid, clan_id });
            } else {
                c.db().pc_clan().insert(PcClan { account_id: aid, clan_id });
            }
        }
        None => {
            c.db().pc_clan().account_id().delete(aid);
        }
    }
}

/// The clan name clif_name shows as the position of a character without a guild (sd->clan->name).
pub fn clan_name_of(c: &Ctx, aid: u32) -> Option<String> {
    sd_clan(c, aid).and_then(clan_search).map(|cl| cl.name.clone())
}

// ------------------------------------------------------------------------------------------------ clan.cpp

/// clan.cpp clan_getavailablesd: the first online member (by slot).
pub fn clan_getavailablesd(c: &Ctx, clan_id: i32) -> Option<u32> {
    let max = clan_search(clan_id).map(|cl| cl.max_member.max(0) as usize).unwrap_or(0);
    state(c, clan_id).members.iter().take(max).copied().find(|&a| a != 0)
}

/// clan.cpp clan_getMemberIndex.
fn clan_get_member_index(s: &ClanState, max: usize, account_id: u32) -> Option<usize> {
    s.members.iter().take(max).position(|&a| a != 0 && a == account_id)
}

/// clan.cpp clan_getNextFreeMemberIndex.
fn clan_get_next_free_member_index(s: &ClanState, max: usize) -> Option<usize> {
    (0..max).find(|&i| s.members.get(i).copied().unwrap_or(0) == 0)
}

/// clan.cpp clan_member_joined: the session joins its clan's online members (pc_reg_received, clan_member_join).
pub fn clan_member_joined(c: &Ctx, aid: u32) {
    if let Some(clan_id) = sd_clan(c, aid) {
        clif_clan_basicinfo(c, aid);
        clif_clan_onlinecount(c, clan_id);
        return;
    }
    let Some(ch) = crate::pc::character_of(c, aid) else { return };
    let Some(clan) = clan_search(ch.clan_id as i32) else { return };
    let max = clan.max_member.max(0) as usize;
    let mut s = state(c, clan.id);
    if let Some(index) = clan_get_next_free_member_index(&s, max) {
        set_sd_clan(c, aid, Some(clan.id));
        if s.members.len() <= index {
            s.members.resize(index + 1, 0);
        }
        s.members[index] = aid;
        s.connect_member += 1;
        put_state(c, &s);
        clif_clan_basicinfo(c, aid);
        // intif_clan_member_joined(clan->id): other_mapserver_count < 1, nothing is sent
        clif_clan_onlinecount(c, clan.id);
    }
}

/// clan.cpp clan_member_left: the session leaves its clan's online members (map_quit, clan_member_leave).
pub fn clan_member_left(c: &Ctx, aid: u32) {
    let Some(clan_id) = sd_clan(c, aid) else { return };
    let max = clan_search(clan_id).map(|cl| cl.max_member.max(0) as usize).unwrap_or(0);
    let mut s = state(c, clan_id);
    if let Some(index) = clan_get_member_index(&s, max, aid) {
        s.members[index] = 0;
        while s.members.last() == Some(&0) {
            s.members.pop();
        }
        s.connect_member -= 1;
        put_state(c, &s);
        // intif_clan_member_left(clan->id): other_mapserver_count < 1, nothing is sent
        clif_clan_onlinecount(c, clan_id);
    }
}

/// clan.cpp clan_member_join (script clan_join).
pub fn clan_member_join(c: &Ctx, aid: u32, clan_id: i32, account_id: u32, char_id: u32) -> bool {
    let Some(clan) = clan_search(clan_id) else { return false };
    let Some(mut ch) = crate::pc::character_of(c, aid) else { return false };
    if ch.account_id != account_id || ch.id != char_id || ch.clan_id != 0 {
        return false;
    }
    if state(c, clan.id).instance_id > 0 && instance_block_invite() {
        return false;
    }
    ch.clan_id = clan.id as u32;
    crate::status::put_char(c, &ch);
    clan_member_joined(c, aid);
    true
}

/// clan.cpp clan_member_leave (script clan_leave).
pub fn clan_member_leave(c: &Ctx, aid: u32, clan_id: i32, account_id: u32, char_id: u32) -> bool {
    let Some(ch) = crate::pc::character_of(c, aid) else { return false };
    if ch.account_id != account_id || ch.id != char_id || ch.clan_id as i32 != clan_id {
        return false;
    }
    let Some(sd_clan_id) = sd_clan(c, aid) else { return false };
    if state(c, sd_clan_id).instance_id > 0 && instance_block_leave() {
        return false;
    }
    clan_member_left(c, aid);
    set_sd_clan(c, aid, None);
    if let Some(mut ch) = crate::pc::character_of(c, aid) {
        ch.clan_id = 0;
        crate::status::put_char(c, &ch);
    }
    clif_clan_leave(c, aid);
    true
}

/// clan.cpp clan_recv_message.
fn clan_recv_message(c: &Ctx, clan_id: i32, mes: &[u8]) {
    if clan_search(clan_id).is_none() {
        return;
    }
    clif_clan_message(c, clan_id, mes);
}

/// clan.cpp clan_send_message: the clan chat (and its chat log).
pub fn clan_send_message(c: &Ctx, aid: u32, mes: &[u8]) {
    if sd_clan(c, aid).is_none() {
        return;
    }
    let Some(ch) = crate::pc::character_of(c, aid) else { return };
    // intif_clan_message: other_mapserver_count < 1, nothing is sent
    clan_recv_message(c, ch.clan_id as i32, mes);
    crate::map_log::log_chat_sd(c, crate::map_log::LOG_CHAT_CLAN, ch.clan_id as i32, aid, None, &crate::net::wire::from_latin1(crate::net::wire::until_nul(mes)));
}

/// clan.cpp clan_get_alliance_count: allies (flag 0) or antagonists (flag 1).
pub fn clan_get_alliance_count(clan: &ClanData, flag: i32) -> usize {
    clan.alliances.iter().take(MAX_CLANALLIANCE).filter(|a| a.clan_id > 0 && a.opposition == flag).count()
}

/// map.cpp map_quit: `if (sd->status.clan_id) clan_member_left(*sd)`.
pub fn map_quit(c: &Ctx, aid: u32) {
    if crate::pc::character_of(c, aid).map(|ch| ch.clan_id != 0).unwrap_or(false) {
        clan_member_left(c, aid);
    }
}

/// unit_free: sd->clan goes with the session.
pub fn unit_free(c: &Ctx, aid: u32) {
    c.db().pc_clan().account_id().delete(aid);
}

/// pc.cpp pc_reg_received: `if (sd->status.clan_id > 0) clan_member_joined(*sd)`; the char server's hourly
/// char_clan_member_cleanup timer is started with the first character (the module has no char server start).
pub fn pc_reg_received(c: &Ctx, aid: u32) {
    ensure_cleanup_timer(c);
    if crate::pc::character_of(c, aid).map(|ch| ch.clan_id > 0).unwrap_or(false) {
        clan_member_joined(c, aid);
    }
}

// ------------------------------------------------------------------------------------------------ char.cpp

/// The id of the char_clan_member_cleanup interval timer (one row).
#[table(accessor = clan_cleanup_timer)]
#[derive(Clone, Debug)]
pub struct ClanCleanupTimer {
    #[primary_key]
    pub id: u8,
    pub timer: u64,
}

fn ensure_cleanup_timer(c: &Ctx) {
    if let Some(t) = c.db().clan_cleanup_timer().id().find(0) {
        if crate::timer::get(c, t.timer).is_some() {
            return;
        }
        c.db().clan_cleanup_timer().id().delete(0);
    }
    let timer = crate::timer::add_interval(c, c.tick() + 1000, crate::timer::Func::ClanMemberCleanup, 0, 0, CLAN_CLEANUP_INTERVAL);
    c.db().clan_cleanup_timer().insert(ClanCleanupTimer { id: 0, timer });
}

/// char.cpp char_clan_member_cleanup: `UPDATE char SET clan_id = 0 WHERE online = 0 AND clan_id <> 0 AND last_login
/// IS NOT NULL AND last_login <= NOW() - INTERVAL clan_remove_inactive_days DAY`.
pub fn char_clan_member_cleanup(c: &Ctx) {
    // Auto removal is disabled
    if CLAN_REMOVE_INACTIVE_DAYS <= 0 {
        return;
    }
    let limit_us = c.rc.timestamp.to_micros_since_unix_epoch() - CLAN_REMOVE_INACTIVE_DAYS * 86_400 * 1_000_000;
    let stale: Vec<crate::character::Character> = c
        .db()
        .character()
        .iter()
        .filter(|ch| ch.clan_id != 0)
        .filter(|ch| c.db().pc().char_id().find(ch.id).is_none()) // online = 0
        .filter(|ch| ch.last_login.map(|t| t.to_micros_since_unix_epoch() <= limit_us).unwrap_or(false))
        .collect();
    for mut ch in stale {
        ch.clan_id = 0;
        c.db().character().id().update(ch);
    }
}

use crate::character::character;
use crate::pc::pc;

// ------------------------------------------------------------------------------------------------ clif.cpp

/// clif.cpp clif_send's CLAN target: every online member of the sender's clan (by slot) whose session is active
/// (enable_spy is off, so no @clanspy listeners).
pub fn send_clan(c: &Ctx, buf: &[u8], sd: u32) {
    let Some(clan_id) = sd_clan(c, sd) else { return };
    let max = clan_search(clan_id).map(|cl| cl.max_member.max(0) as usize).unwrap_or(0);
    for aid in state(c, clan_id).members.into_iter().take(max) {
        if aid == 0 {
            continue;
        }
        if let Some(id) = crate::pc::identity_of(c, aid) {
            c.send(id, buf);
        }
    }
}

/// mapindex.cpp mapindex_getmapname_ext: the map name (the part after '#' of an instance name, at most
/// MAP_NAME_LENGTH - 1 characters) with ".gat".
pub(crate) fn mapindex_getmapname_ext(s: &str) -> String {
    let mut buf = crate::net::wire::latin1(s);
    // sscanf("%*[^#]%*[#]%15s"): only when at least one character precedes the '#'
    if let Some(p) = buf.iter().position(|&b| b == b'#').filter(|&p| p > 0) {
        let rest: Vec<u8> = buf[p..].iter().copied().skip_while(|&b| b == b'#').take_while(|&b| !b.is_ascii_whitespace()).take(15).collect();
        if !rest.is_empty() {
            buf = rest;
        }
    }
    let nul = buf.iter().position(|&b| b == 0).unwrap_or(buf.len());
    buf.truncate(nul);
    let mut len = buf.len().min(MAP_NAME_LENGTH);
    if len == MAP_NAME_LENGTH {
        log::warn!("(mapindex_normalize_name) Map name '{}' is too long!", crate::net::wire::from_latin1(&buf));
        len -= 1;
    }
    buf.truncate(len);
    if len < 4 || !buf[len - 4..].eq_ignore_ascii_case(b".gat") {
        buf.extend_from_slice(b".gat");
    }
    crate::net::wire::from_latin1(&buf)
}

/// clif.cpp clif_clan_basicinfo: ZC_CLANINFO (0x098a) with the allies then the antagonists (24 bytes each).
pub fn clif_clan_basicinfo(c: &Ctx, aid: u32) {
    let Some(clan) = sd_clan(c, aid).and_then(clan_search) else { return };
    let mut w = Vec::with_capacity(74 + 24 * MAX_CLANALLIANCE);
    w.extend_from_slice(&0x098au16.to_le_bytes());
    w.extend_from_slice(&0u16.to_le_bytes());
    w.extend_from_slice(&(clan.id as u32).to_le_bytes());
    crate::net::wire::put_str(&mut w, &clan.name, NAME_LENGTH);
    crate::net::wire::put_str(&mut w, &clan.master, NAME_LENGTH);
    crate::net::wire::put_str(&mut w, &mapindex_getmapname_ext(&clan.map), 16);
    w.push(clan_get_alliance_count(clan, 0) as u8);
    w.push(clan_get_alliance_count(clan, 1) as u8);
    for flag in 0..2 {
        for a in clan.alliances.iter().take(MAX_CLANALLIANCE) {
            if a.clan_id > 0 && a.opposition == flag {
                crate::net::wire::put_str(&mut w, &a.name, NAME_LENGTH);
            }
        }
    }
    let len = w.len() as u16;
    w[2..4].copy_from_slice(&len.to_le_bytes());
    if let Some(u) = crate::unit::get(c, aid) {
        crate::clif::send(c, &w, &u, crate::clif::Target::SelfOnly);
    }
}

/// clif.cpp clif_clan_onlinecount: ZC_NOTIFY_CLAN_CONNECTINFO (0x0988) to the clan (through its first online
/// member; none: not sent).
pub fn clif_clan_onlinecount(c: &Ctx, clan_id: i32) {
    let Some(sd) = clan_getavailablesd(c, clan_id) else { return };
    let Some(clan) = clan_search(clan_id) else { return };
    let s = state(c, clan_id);
    let mut w = Vec::with_capacity(6);
    w.extend_from_slice(&0x0988u16.to_le_bytes());
    w.extend_from_slice(&s.connect_member.to_le_bytes());
    w.extend_from_slice(&clan.max_member.to_le_bytes());
    send_clan(c, &w, sd);
}

/// clif.cpp clif_clan_leave: ZC_ACK_CLAN_LEAVE (0x0989).
pub fn clif_clan_leave(c: &Ctx, aid: u32) {
    if let Some(u) = crate::unit::get(c, aid) {
        crate::clif::send(c, &0x0989u16.to_le_bytes(), &u, crate::clif::Target::SelfOnly);
    }
}

/// clif.cpp clif_clan_message: ZC_NOTIFY_CLAN_CHAT (0x098e): an empty member name ("it's in the message too") and
/// the message (up to its first NUL, zero-padded to len + 1 bytes), to the clan through its first online member.
fn clif_clan_message(c: &Ctx, clan_id: i32, mes: &[u8]) {
    let sd = clan_getavailablesd(c, clan_id);
    let mut len = mes.len();
    if len == 0 {
        return;
    }
    // Maximum message length = size of our packet buffer (UINT16_MAX) minus the fixed size of the packet and 1 byte
    let len_max = u16::MAX as usize - 28 - 1;
    if len > len_max {
        log::warn!("clif_clan_message: Truncated message (len={len}, max={len_max}, clan_id={clan_id}).");
        len = len_max;
    }
    let mut w = Vec::with_capacity(28 + len + 1);
    w.extend_from_slice(&0x098eu16.to_le_bytes());
    w.extend_from_slice(&((28 + len + 1) as u16).to_le_bytes());
    w.extend_from_slice(&[0u8; NAME_LENGTH]);
    // safestrncpy(p->Message, mes, len + 1)
    let body: Vec<u8> = mes.iter().copied().take(len).take_while(|&b| b != 0).collect();
    w.extend_from_slice(&body);
    w.resize(28 + len + 1, 0);
    if let Some(sd) = sd {
        send_clan(c, &w, sd);
    }
}

/// clif.cpp clif_parse_clan_chat (CZ_CLAN_CHAT 0x098d "<name> : <message>"): validated like every chat message,
/// then the whole text goes to the clan.
pub fn parse_clan_chat(c: &Ctx, aid: u32, pkt: &[u8]) {
    // validate packet and retrieve name and message
    if crate::parse::process_message(c, aid, pkt, false).is_none() {
        return;
    }
    let Some(len) = crate::net::wire::Reader::at(pkt, 2).u16() else { return };
    let len = (len as usize).saturating_sub(4);
    let text = pkt.get(4..(4 + len).min(pkt.len())).unwrap_or(&[]);
    clan_send_message(c, aid, &text[..len.min(text.len())]);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn default_clans() {
        let db = clan_db();
        assert_eq!(db.clans.len(), 4);
        let sword = clan_search(1).unwrap();
        assert_eq!((sword.name.as_str(), sword.master.as_str(), sword.map.as_str(), sword.max_member), ("Swordman Clan", "Raffam Oranpere", "prontera", 500));
        let arc = clan_search(2).unwrap();
        assert_eq!((clan_get_alliance_count(arc, 0), clan_get_alliance_count(arc, 1)), (1, 1));
        assert_eq!(clan_search(3).unwrap().alliances.len(), 3);
        assert_eq!(clan_searchname("crossbow clan").map(|c| c.id), Some(4));
    }

    #[test]
    fn map_ext() {
        assert_eq!(mapindex_getmapname_ext("prontera"), "prontera.gat");
        assert_eq!(mapindex_getmapname_ext("izlude.gat"), "izlude.gat");
        // strnlen(buf, MAP_NAME_LENGTH) == 12 counts as "too long": cut to 11, then ".gat" again
        assert_eq!(mapindex_getmapname_ext("prontera.gat"), "prontera.ga.gat");
        assert_eq!(mapindex_getmapname_ext("1@tower#abc"), "abc.gat");
        assert_eq!(mapindex_getmapname_ext(""), ".gat");
    }
}
