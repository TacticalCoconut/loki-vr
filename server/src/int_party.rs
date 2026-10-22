//! char/int_party.cpp: the char server's side of parties — the `party` SQL table, the char server's party_db cache
//! (struct party_data: the party plus min/max level, family and size) and the inter-server requests the map server
//! sends (intif.rs queues them; answers go back through the same queue to party.rs).
//!
//! Two copies of every party exist, as in rAthena: this one (the char server's) and the map server's party_db
//! (party.rs `party_data`), which is updated only from the answers sent here.
//!
//! `char`.`party_id` writes: rAthena's char server updates the SQL column while the map server keeps its own copy in
//! sd->status.party_id and writes that back whenever it saves the character. The module has one `character` row, so
//! these SQL writes are applied to characters that are not online; for an online character the map server's value
//! (party.rs sets it when it handles the answer) is the one rAthena would end up saving.

use spacetimedb::{table, SpacetimeType, Table};

use crate::character::character;
use crate::party::{PartyMember, MAX_PARTY};
use crate::intif::{self, Inter};
use crate::pc::pc as _;
use crate::world::Ctx;

/// common/mmo.hpp struct party (exp is a 1-bit field, item a 2-bit field: stores are masked like the bitfields).
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct Party {
    pub party_id: u32,
    pub name: String,
    /// Count of online characters (unsigned char: wraps).
    pub count: u8,
    pub exp: u8,
    pub item: u8,
    /// Always MAX_PARTY entries; an empty slot has account_id 0.
    pub member: Vec<PartyMember>,
}

impl Party {
    pub fn empty(party_id: u32) -> Party {
        Party { party_id, member: vec![PartyMember::default(); MAX_PARTY], ..Default::default() }
    }
}

/// rAthena `party` table (sql-files/main.sql).
#[table(accessor = party)]
#[derive(Clone, Debug)]
pub struct PartySql {
    #[primary_key]
    pub party_id: u32,
    pub name: String,
    pub exp: u8,
    pub item: u8,
    pub leader_id: u32,
    pub leader_char: u32,
}

/// int_party.cpp struct party_data: the char server's party_db (loaded from SQL on first use, kept while the char
/// server runs).
#[table(accessor = inter_party)]
#[derive(Clone, Debug)]
pub struct InterParty {
    #[primary_key]
    pub party_id: u32,
    pub party: Party,
    pub min_lv: u32,
    pub max_lv: u32,
    /// Is this party a family? If so, this holds the child id.
    pub family: u32,
    /// Total size of party (unsigned char).
    pub size: u8,
}

/// Char-server settings that change at run time.
#[table(accessor = inter_party_config)]
pub struct InterPartyConfig {
    #[primary_key]
    pub key: u8,
    /// conf/inter_athena.conf `party_share_level: 15` (changed by @partysharelvl through intif_party_sharelvlupdate).
    pub party_share_level: u32,
}

/// conf/inter_athena.conf `party_share_level`.
pub const PARTY_SHARE_LEVEL: u32 = 15;

pub fn party_share_level(c: &Ctx) -> u32 {
    c.db().inter_party_config().key().find(0).map(|r| r.party_share_level).unwrap_or(PARTY_SHARE_LEVEL)
}

/// inter_party_tosql flags (int_party.hpp).
pub const PS_CREATE: u32 = 0x01;
pub const PS_BASIC: u32 = 0x02;
pub const PS_LEADER: u32 = 0x04;
pub const PS_ADDMEMBER: u32 = 0x08;
pub const PS_DELMEMBER: u32 = 0x10;
pub const PS_BREAK: u32 = 0x20;

/// conf/char_athena.conf `char_name_option: 1` with `char_name_letters` (also checked for party names).
const CHAR_NAME_OPTION: i32 = 1;

fn put_cache(c: &Ctx, p: &InterParty) {
    if c.db().inter_party().party_id().find(p.party_id).is_some() {
        c.db().inter_party().party_id().update(p.clone());
    } else {
        c.db().inter_party().insert(p.clone());
    }
}

fn char_online(c: &Ctx, char_id: u32) -> bool {
    c.db().pc().char_id().find(char_id).is_some()
}

/// `UPDATE char SET party_id = … WHERE …` for one character (see the module doc: offline characters only).
fn sql_set_char_party(c: &Ctx, char_id: u32, account_id: Option<u32>, only_if_party: Option<u32>, party_id: u32) {
    let Some(mut ch) = c.db().character().id().find(char_id) else { return };
    if account_id.is_some_and(|a| a != ch.account_id) || only_if_party.is_some_and(|p| p != ch.party_id) {
        return;
    }
    if char_online(c, char_id) || ch.party_id == party_id {
        return;
    }
    ch.party_id = party_id;
    c.db().character().id().update(ch);
}

/// int_party.cpp int_party_check_lv: updates the party's level range and unsets even share if broken.
fn int_party_check_lv(c: &Ctx, p: &mut InterParty) -> bool {
    p.min_lv = u32::MAX;
    p.max_lv = 0;
    for m in p.party.member.iter() {
        // If not online (doesn't affect exp range)
        if !m.online {
            continue;
        }
        let lv = m.lv as u32;
        if lv < p.min_lv {
            p.min_lv = lv;
        }
        if lv > p.max_lv {
            p.max_lv = lv;
        }
    }
    if p.party.exp != 0 && !party_check_exp_share(c, p) {
        p.party.exp = 0;
        mapif_party_optionchanged(&p.party, 0, 0);
        return false;
    }
    true
}

/// char.cpp char_child.
fn char_child(c: &Ctx, parent_id: u32, child_id: u32) -> bool {
    if parent_id == 0 || child_id == 0 {
        return false;
    }
    c.db().character().id().find(parent_id).map(|ch| ch.child == child_id).unwrap_or(false)
}

/// char.cpp char_family: the child's id when the three characters are a couple and their child.
fn char_family(c: &Ctx, cid1: u32, cid2: u32, cid3: u32) -> u32 {
    if cid1 == 0 || cid2 == 0 || cid3 == 0 {
        return 0;
    }
    let mut ids = [cid1, cid2, cid3];
    ids.sort_unstable(); // `WHERE char_id IN (...)` returns the rows in primary-key order
    let mut seen = Vec::new();
    for id in ids {
        if seen.contains(&id) {
            continue;
        }
        seen.push(id);
        let Some(ch) = c.db().character().id().find(id) else { continue };
        let (charid, partnerid, childid) = (ch.id, ch.partner_id, ch.child);
        if (cid1 == charid && ((cid2 == partnerid && cid3 == childid) || (cid2 == childid && cid3 == partnerid)))
            || (cid1 == partnerid && ((cid2 == charid && cid3 == childid) || (cid2 == childid && cid3 == charid)))
            || (cid1 == childid && ((cid2 == charid && cid3 == partnerid) || (cid2 == partnerid && cid3 == charid)))
        {
            return childid;
        }
    }
    0
}

/// int_party.cpp int_party_calc_state: the state of a party (size, online count, family, level range).
fn int_party_calc_state(c: &Ctx, p: &mut InterParty) {
    p.min_lv = u32::MAX;
    p.max_lv = 0;
    p.party.count = 0;
    p.size = 0;
    p.family = 0;
    // Check party size
    for m in p.party.member.iter() {
        if m.lv == 0 {
            continue;
        }
        p.size = p.size.wrapping_add(1);
        if m.online {
            p.party.count = p.party.count.wrapping_add(1);
        }
    }
    let m0 = p.party.member[0].clone();
    let m1 = p.party.member[1].clone();
    if p.size == 2 && (char_child(c, m0.char_id, m1.char_id) || char_child(c, m1.char_id, m0.char_id)) {
        // Child should be able to share with either of their parents [RoM]
        if m0.class & 0x2000 != 0 {
            // first slot is the child?
            p.family = m0.char_id;
        } else {
            p.family = m1.char_id;
        }
    } else if p.size == 3 {
        // Check Family State.
        p.family = char_family(c, m0.char_id, m1.char_id, p.party.member[2].char_id);
    }
    // max/min levels.
    for m in p.party.member.iter() {
        let lv = m.lv as u32;
        if lv == 0 {
            continue;
        }
        if m.online {
            if lv < p.min_lv {
                p.min_lv = lv;
            }
            if p.max_lv < lv {
                p.max_lv = lv;
            }
        }
    }
    if p.party.exp != 0 && !party_check_exp_share(c, p) {
        p.party.exp = 0; // Set off even share.
        mapif_party_optionchanged(&p.party, 0, 0);
    }
}

/// int_party.cpp inter_party_tosql: saves the party to SQL. `index` is the member the flag is about.
fn inter_party_tosql(c: &Ctx, p: &mut Party, flag: u32, index: usize) -> bool {
    if p.party_id == 0 {
        return false;
    }
    let mut party_id = p.party_id;
    if flag & PS_BREAK != 0 {
        // Break the party: we'll skip name-checking and just reset everyone with the same party id [celest]
        let ids: Vec<u32> = c.db().character().iter().filter(|ch| ch.party_id == party_id).map(|ch| ch.id).collect();
        for id in ids {
            sql_set_char_party(c, id, None, Some(party_id), 0);
        }
        c.db().party().party_id().delete(party_id);
        // Remove from memory
        c.db().inter_party().party_id().delete(party_id);
        return true;
    }
    if flag & PS_CREATE != 0 {
        // Create party: INSERT … (auto_increment party_id)
        party_id = crate::ids::next(c.rc, "party", 1);
        let m = &p.member[index];
        c.db().party().insert(PartySql { party_id, name: p.name.clone(), exp: p.exp, item: p.item, leader_id: m.account_id, leader_char: m.char_id });
        p.party_id = party_id;
    }
    if flag & PS_BASIC != 0 {
        if let Some(row) = c.db().party().party_id().find(party_id) {
            c.db().party().party_id().update(PartySql { name: p.name.clone(), exp: p.exp, item: p.item, ..row });
        }
    }
    if flag & PS_LEADER != 0 {
        if let Some(row) = c.db().party().party_id().find(party_id) {
            let m = &p.member[index];
            c.db().party().party_id().update(PartySql { leader_id: m.account_id, leader_char: m.char_id, ..row });
        }
    }
    if flag & PS_ADDMEMBER != 0 {
        let m = &p.member[index];
        sql_set_char_party(c, m.char_id, Some(m.account_id), None, party_id);
    }
    if flag & PS_DELMEMBER != 0 {
        let m = &p.member[index];
        sql_set_char_party(c, m.char_id, Some(m.account_id), Some(party_id), 0);
    }
    true
}

/// int_party.cpp inter_party_fromsql: the party from the cache, or loaded from SQL into it.
pub fn inter_party_fromsql(c: &Ctx, party_id: u32) -> Option<InterParty> {
    if party_id == 0 {
        return None;
    }
    // Load from memory
    if let Some(p) = c.db().inter_party().party_id().find(party_id) {
        return Some(p);
    }
    let row = c.db().party().party_id().find(party_id)?;
    let mut party = Party::empty(party_id);
    party.name = row.name.clone();
    party.exp = (row.exp != 0) as u8;
    party.item = row.item & 3;
    // Load members: `SELECT … FROM char WHERE party_id = …` (by the party_id index: char_id order)
    let mut chars: Vec<_> = c.db().character().iter().filter(|ch| ch.party_id == party_id).collect();
    chars.sort_by_key(|ch| ch.id);
    for (i, ch) in chars.into_iter().take(MAX_PARTY).enumerate() {
        party.member[i] = PartyMember {
            account_id: ch.account_id,
            char_id: ch.id,
            name: ch.name.clone(),
            lv: ch.base_level,
            map: ch.last_map.clone(),
            online: char_online(c, ch.id),
            class: ch.class,
            leader: ch.account_id == row.leader_id && ch.id == row.leader_char,
        };
    }
    let mut p = InterParty { party_id, party, min_lv: 0, max_lv: 0, family: 0, size: 0 };
    // init state
    int_party_calc_state(c, &mut p);
    put_cache(c, &p);
    Some(p)
}

/// int_party.cpp search_partyname (`WHERE name = '…'`: MySQL compares without regard to case).
fn search_partyname(c: &Ctx, name: &str) -> Option<InterParty> {
    let key = crate::net::wire::latin1(name);
    let key: Vec<u8> = key.into_iter().take(crate::party::NAME_LENGTH).collect();
    let trim = |b: &[u8]| -> Vec<u8> {
        // PAD SPACE collation: trailing spaces do not count
        let mut v = b.to_vec();
        while v.last() == Some(&b' ') {
            v.pop();
        }
        v
    };
    let key = trim(&key);
    let mut rows: Vec<PartySql> = c.db().party().iter().filter(|r| trim(&crate::net::wire::latin1(&r.name)).eq_ignore_ascii_case(&key)).collect();
    rows.sort_by_key(|r| r.party_id);
    let row = rows.into_iter().next()?;
    inter_party_fromsql(c, row.party_id)
}

/// int_party.cpp party_check_family_share.
fn party_check_family_share(p: &InterParty) -> bool {
    if p.family == 0 {
        return false;
    }
    let Some(map) = p.party.member.iter().find(|m| m.char_id == p.family).map(|m| m.map.clone()) else { return false };
    for mem in p.party.member.iter() {
        if mem.lv == 0 {
            continue;
        }
        if p.family == mem.char_id {
            continue;
        }
        if !mem.online {
            // everyone should be online to share
            return false;
        }
        if mem.map != map {
            // everyone should be on the same map
            return false;
        }
        if mem.lv < 70 {
            // parents must both be above 70
            return false;
        }
    }
    true
}

/// int_party.cpp party_check_exp_share: whether this party can keep having exp share.
fn party_check_exp_share(c: &Ctx, p: &InterParty) -> bool {
    p.party.count < 2 || p.max_lv.wrapping_sub(p.min_lv) <= party_share_level(c) || party_check_family_share(p)
}

/// int_party.cpp party_check_empty: breaks the party when nobody is left in it.
fn party_check_empty(c: &Ctx, p: &mut InterParty) -> bool {
    if p.party.party_id == 0 {
        return true;
    }
    if p.party.member.iter().any(|m| m.account_id != 0) {
        return false;
    }
    // If there is no member, then break the party
    mapif_party_broken(p.party.party_id, 0);
    inter_party_tosql(c, &mut p.party, PS_BREAK, 0);
    true
}

// ------------------------------------------------------------------------------------------ to the map server

fn mapif_party_created(account_id: u32, char_id: u32, p: Option<&Party>) {
    if let Some(p) = p {
        log::info!("int_party: Party created ({} - {})", p.party_id, p.name);
    }
    intif::send(Inter::PartyCreated { account_id, char_id, party: p.map(|p| (p.party_id, p.name.clone())) });
}

fn mapif_party_noinfo(party_id: u32, char_id: u32) {
    log::warn!("int_party: info not found (party_id={party_id} char_id={char_id})");
    intif::send(Inter::PartyInfoAck { party_id, char_id, party: None });
}

fn mapif_party_info(p: &Party, char_id: u32) {
    intif::send(Inter::PartyInfoAck { party_id: p.party_id, char_id, party: Some(p.clone()) });
}

fn mapif_party_memberadded(party_id: u32, account_id: u32, char_id: u32, flag: u8) {
    intif::send(Inter::PartyMemberAdded { party_id, account_id, char_id, flag });
}

/// mapif_party_optionchanged (sent to every map server when flag is 0, to the requester otherwise: one map server).
fn mapif_party_optionchanged(p: &Party, account_id: u32, flag: u8) {
    intif::send(Inter::PartyOptionChanged { party_id: p.party_id, account_id, exp: p.exp as u16, item: p.item as u16, flag });
}

fn mapif_party_withdraw(party_id: u32, account_id: u32, char_id: u32, name: &str, kind: u8) {
    intif::send(Inter::PartyWithdraw { party_id, account_id, char_id, name: name.to_string(), kind });
}

fn mapif_party_membermoved(p: &Party, idx: usize) {
    let m = &p.member[idx];
    intif::send(Inter::PartyMove { party_id: p.party_id, account_id: m.account_id, char_id: m.char_id, online: m.online as u8, lv: m.lv, map: m.map.clone() });
}

fn mapif_party_broken(party_id: u32, _flag: u8) {
    intif::send(Inter::PartyBroken { party_id });
}

// ------------------------------------------------------------------------------------------ from the map server

/// int_party.cpp mapif_parse_CreateParty.
pub fn mapif_parse_create_party(c: &Ctx, name: String, item: u8, item2: u8, leader: PartyMember) {
    if search_partyname(c, &name).is_some() {
        mapif_party_created(leader.account_id, leader.char_id, None);
        return;
    }
    // Check Authorised letters/symbols in the name of the character
    let nb = crate::net::wire::latin1(&name);
    if CHAR_NAME_OPTION == 1 {
        // only letters/symbols in char_name_letters are authorised
        for &ch in nb.iter().take(crate::party::NAME_LENGTH) {
            if !crate::rules::CHAR_NAME_LETTERS.as_bytes().contains(&ch) {
                if ch == b'"' {
                    // client-special-char
                    let normalized = normalize_name(&nb, b"\"");
                    mapif_parse_create_party(c, crate::net::wire::from_latin1(&normalized), item, item2, leader);
                    return;
                }
                mapif_party_created(leader.account_id, leader.char_id, None);
                return;
            }
        }
    }
    let mut party = Party::empty(0);
    party.name = crate::net::wire::from_latin1(&nb.iter().copied().take(crate::party::NAME_LENGTH - 1).collect::<Vec<u8>>());
    party.exp = 0;
    party.item = (item != 0) as u8 | (((item2 != 0) as u8) << 1);
    party.member[0] = leader.clone();
    party.member[0].leader = true;
    party.member[0].online = true;
    party.party_id = u32::MAX; // New party (-1).
    let mut p = InterParty { party_id: 0, party, min_lv: 0, max_lv: 0, family: 0, size: 0 };
    if inter_party_tosql(c, &mut p.party, PS_CREATE | PS_ADDMEMBER, 0) {
        // Add party to db
        p.party_id = p.party.party_id;
        int_party_calc_state(c, &mut p);
        put_cache(c, &p);
        mapif_party_info(&p.party, 0);
        mapif_party_created(leader.account_id, leader.char_id, Some(&p.party));
    } else {
        // Failed to create party.
        mapif_party_created(leader.account_id, leader.char_id, None);
    }
}

/// strlib.cpp normalize_name: trims `delims` from both ends and turns runs of them into one space.
fn normalize_name(s: &[u8], delims: &[u8]) -> Vec<u8> {
    let mut out = Vec::with_capacity(s.len());
    let mut i = 0;
    while i < s.len() && delims.contains(&s[i]) {
        i += 1;
    }
    let mut put_space = false;
    while i < s.len() {
        if put_space {
            out.push(b' ');
        }
        while i < s.len() && !delims.contains(&s[i]) {
            out.push(s[i]);
            i += 1;
        }
        while i < s.len() && delims.contains(&s[i]) {
            i += 1;
        }
        put_space = true;
    }
    out
}

/// int_party.cpp mapif_parse_PartyInfo.
pub fn mapif_parse_party_info(c: &Ctx, party_id: u32, char_id: u32) {
    match inter_party_fromsql(c, party_id) {
        Some(p) => mapif_party_info(&p.party, char_id),
        None => mapif_party_noinfo(party_id, char_id),
    }
}

/// int_party.cpp mapif_parse_PartyAddMember.
pub fn mapif_parse_party_add_member(c: &Ctx, party_id: u32, member: PartyMember) {
    let Some(mut p) = inter_party_fromsql(c, party_id).filter(|p| p.size as usize != MAX_PARTY) else {
        mapif_party_memberadded(party_id, member.account_id, member.char_id, 1);
        return;
    };
    let Some(i) = p.party.member.iter().position(|m| m.account_id == 0) else {
        // Party full
        mapif_party_memberadded(party_id, member.account_id, member.char_id, 1);
        return;
    };
    p.party.member[i] = member.clone();
    p.party.member[i].leader = false;
    if p.party.member[i].online {
        p.party.count = p.party.count.wrapping_add(1);
    }
    p.size = p.size.wrapping_add(1);
    if p.size == 2 || p.size == 3 {
        // Check family state. And also accept either of their Parents. [RoM]
        int_party_calc_state(c, &mut p);
    } else if (member.lv as u32) < p.min_lv || (member.lv as u32) > p.max_lv || p.family != 0 {
        // Check even share range.
        if p.family != 0 {
            p.family = 0; // Family state broken.
        }
        int_party_check_lv(c, &mut p);
    }
    mapif_party_info(&p.party, 0);
    mapif_party_memberadded(party_id, member.account_id, member.char_id, 0);
    inter_party_tosql(c, &mut p.party, PS_ADDMEMBER, i);
    put_cache(c, &p);
}

/// int_party.cpp mapif_parse_PartyChangeOption.
pub fn mapif_parse_party_change_option(c: &Ctx, party_id: u32, account_id: u32, exp: u16, item: u16) {
    let Some(mut p) = inter_party_fromsql(c, party_id) else { return };
    let mut flag = 0u8;
    if p.size == 2 || p.size == 3 {
        // check family state. Also accept either of their parents.
        int_party_calc_state(c, &mut p);
    }
    p.party.exp = (exp & 1) as u8;
    if exp != 0 && !party_check_exp_share(c, &p) {
        flag |= 0x01;
        p.party.exp = 0;
    }
    p.party.item = (item & 0x3) as u8; // Filter out invalid values.
    mapif_party_optionchanged(&p.party, account_id, flag);
    inter_party_tosql(c, &mut p.party, PS_BASIC, 0);
    put_cache(c, &p);
}

/// int_party.cpp mapif_parse_PartyLeave (also inter_party_leave for a deleted character).
pub fn mapif_parse_party_leave(c: &Ctx, party_id: u32, account_id: u32, char_id: u32, name: &str, kind: u8) {
    let Some(mut p) = inter_party_fromsql(c, party_id) else {
        // Party does not exists?
        let ids: Vec<u32> = c.db().character().iter().filter(|ch| ch.party_id == party_id).map(|ch| ch.id).collect();
        for id in ids {
            sql_set_char_party(c, id, None, Some(party_id), 0);
        }
        return;
    };
    let Some(i) = p.party.member.iter().position(|m| m.account_id == account_id && m.char_id == char_id) else {
        return; // Member not found?
    };
    mapif_party_withdraw(party_id, account_id, char_id, name, kind);
    if p.party.member[i].leader {
        // TODO: Official allow 'leaderless' party
        p.party.member[i].account_id = 0;
        for j in 0..MAX_PARTY {
            if p.party.member[j].account_id == 0 {
                continue;
            }
            let m = p.party.member[j].clone();
            mapif_party_withdraw(party_id, m.account_id, m.char_id, &m.name, kind);
            p.party.member[j].account_id = 0;
        }
        // Party gets deleted on the check_empty call below.
    } else {
        inter_party_tosql(c, &mut p.party, PS_DELMEMBER, i);
        let j = p.party.member[i].lv as u32;
        if p.party.member[i].online {
            p.party.count = p.party.count.wrapping_sub(1);
        }
        p.party.member[i] = PartyMember::default();
        p.size = p.size.wrapping_sub(1);
        if j == p.min_lv || j == p.max_lv || p.family != 0 {
            if p.family != 0 {
                p.family = 0; // Family state broken.
            }
            int_party_check_lv(c, &mut p);
        }
    }
    if !party_check_empty(c, &mut p) {
        put_cache(c, &p);
        mapif_party_info(&p.party, 0);
    }
}

/// int_party.cpp mapif_parse_PartyChangeMap: a member changed maps, logged in or out, or levelled.
pub fn mapif_parse_party_change_map(c: &Ctx, party_id: u32, account_id: u32, char_id: u32, online: u8, lv: u16, map: &str) {
    let Some(mut p) = inter_party_fromsql(c, party_id) else { return };
    let Some(i) = p.party.member.iter().position(|m| m.account_id == account_id && m.char_id == char_id) else { return };
    let online = online != 0;
    let lv32 = lv as u32;
    if p.party.member[i].online != online {
        p.party.member[i].online = online;
        if online {
            p.party.count = p.party.count.wrapping_add(1);
        } else {
            p.party.count = p.party.count.wrapping_sub(1);
        }
        // Even share check situations: Family state (always breaks)
        // character logging on/off is max/min level (update level range)
        // or character logging on/off has a different level (update level range using new level)
        let mlv = p.party.member[i].lv as u32;
        if p.family != 0 || (mlv <= p.min_lv || mlv >= p.max_lv) || (mlv != lv32 && (lv32 <= p.min_lv || lv32 >= p.max_lv)) {
            p.party.member[i].lv = lv;
            int_party_check_lv(c, &mut p);
        }
        // Send online/offline update.
        mapif_party_membermoved(&p.party, i);
    }
    if p.party.member[i].lv != lv {
        let mlv = p.party.member[i].lv as u32;
        if mlv == p.min_lv || mlv == p.max_lv {
            p.party.member[i].lv = lv;
            int_party_check_lv(c, &mut p);
        } else {
            p.party.member[i].lv = lv;
        }
        // There is no need to send level update to map servers since they do nothing with it.
    }
    if p.party.member[i].map != map {
        p.party.member[i].map = map.to_string();
        mapif_party_membermoved(&p.party, i);
        int_party_check_lv(c, &mut p);
    }
    put_cache(c, &p);
}

/// int_party.cpp mapif_parse_BreakParty. rAthena calls `mapif_party_broken(fd, party_id)` here (int_party.cpp:751)
/// with the arguments swapped against its definition `mapif_party_broken(party_id, flag)` (int_party.cpp:476): the
/// map servers would be told that party "fd" broke, with the real id as the flag byte. Nothing reaches this function
/// on either side (the map server's intif_break_party, intif.cpp:729, has no caller; nothing here sends
/// Inter::BreakParty), so the module keeps the intended call instead of reproducing the swap.
pub fn mapif_parse_break_party(c: &Ctx, party_id: u32) {
    let Some(mut p) = inter_party_fromsql(c, party_id) else { return };
    inter_party_tosql(c, &mut p.party, PS_BREAK, 0);
    mapif_party_broken(party_id, 0);
}

/// int_party.cpp mapif_parse_PartyLeaderChange.
pub fn mapif_parse_party_leader_change(c: &Ctx, party_id: u32, account_id: u32, char_id: u32) {
    let Some(mut p) = inter_party_fromsql(c, party_id) else { return };
    for i in 0..MAX_PARTY {
        if p.party.member[i].leader {
            p.party.member[i].leader = false;
        }
        if p.party.member[i].account_id == account_id && p.party.member[i].char_id == char_id {
            p.party.member[i].leader = true;
            inter_party_tosql(c, &mut p.party, PS_LEADER, i);
        }
    }
    put_cache(c, &p);
}

/// int_party.cpp mapif_parse_PartyShareLevel: updates the share range of every party with more than one member online.
pub fn mapif_parse_party_share_level(c: &Ctx, share_lvl: u32) {
    if c.db().inter_party_config().key().find(0).is_some() {
        c.db().inter_party_config().key().update(InterPartyConfig { key: 0, party_share_level: share_lvl });
    } else {
        c.db().inter_party_config().insert(InterPartyConfig { key: 0, party_share_level: share_lvl });
    }
    // Update online parties (unordered_map iteration: any order; the answers go to different parties)
    let parties: Vec<InterParty> = c.db().inter_party().iter().collect();
    for mut p in parties {
        if p.party.count > 1 {
            int_party_calc_state(c, &mut p);
            put_cache(c, &p);
        }
    }
}

/// int_party.cpp inter_party_leave: leave request from the char server itself (character deletion).
pub fn inter_party_leave(c: &Ctx, party_id: u32, account_id: u32, char_id: u32, name: &str) {
    mapif_parse_party_leave(c, party_id, account_id, char_id, name, crate::party::PARTY_MEMBER_WITHDRAW_EXPEL);
}
