//! Fame and the rankings: pc.cpp pc_addfame / pc_famerank; the char server's fame lists (char.cpp
//! char_read_fame_list, char_mapif.cpp chmapif_parse_updfamelist / chmapif_send_fame_list /
//! chmapif_update_fame_list / chmapif_parse_reqfamelist) and the map server's copies (chrif.cpp chrif_updatefamelist
//! / chrif_buildfamelist / chrif_recvfamelist / chrif_updatefamelist_ack); clif.cpp clif_ranklist (ZC_ACK_RANKING2
//! 0x0af6 for this client), clif_parse_ranklist (CZ_REQ_RANKING 0x097c) and clif_update_rankingpoint
//! (ZC_UPDATE_RANKING_POINT 0x097e); the script commands addfame / getfame / getfamerank (buildins/extras.rs) and
//! @addfame (atcommand_extras.rs).
//!
//! State: `fame_list` holds both sides of the link — the char server's lists (built by char_read_fame_list, kept in
//! its memory and changed in place by chmapif_parse_updfamelist) and the map server's copies (replaced by 0x2b1b,
//! patched by 0x2b22), which pc_famerank reads. Messages between them go over the inter link (intif.rs). rAthena
//! builds the char lists when the char server starts and sends them when the map server connects; the module has no
//! start, so both are built the first time the lists are needed.
//!
//! Callers elsewhere: pc_jobchange (skilltree agent) reads pc_famerank before the change and calls
//! [`chrif_buildfamelist`] after it; mob.cpp's Taekwon mission (mob agent) and skill.cpp's forging / pharmacy (skills)
//! call [`pc_addfame`] with FAME_TAEKWON_MISSION / FAME_FORGE / FAME_PHARMACY_*.

use spacetimedb::{table, SpacetimeType, Table};

use crate::db::jobs::*;
use crate::intif::{self, Inter};
use crate::world::Ctx;

/// mmo.hpp enum e_rank.
pub const RANK_BLACKSMITH: u8 = 0;
pub const RANK_ALCHEMIST: u8 = 1;
pub const RANK_TAEKWON: u8 = 2;
pub const RANK_KILLER: u8 = 3;

/// mmo.hpp MAX_FAME, MAX_FAME_LIST; conf/char_athena.conf `fame_list_alchemist: 10`, `fame_list_blacksmith: 10`,
/// `fame_list_taekwon: 10`.
pub const MAX_FAME: i64 = 1_000_000_000;
pub const MAX_FAME_LIST: usize = 10;
const FAME_LIST_SIZE_SMITH: usize = 10;
const FAME_LIST_SIZE_CHEMIST: usize = 10;
const FAME_LIST_SIZE_TAEKWON: usize = 10;

/// conf/battle/player.conf `fame_taekwon_mission: 1`, `fame_refine_lv1: 1`, `fame_refine_lv2: 25`,
/// `fame_refine_lv3: 1000`, `fame_forge: 10`, `blacksmith_fame_refine_threshold: 10`, `fame_pharmacy_3: 1`,
/// `fame_pharmacy_5: 3`, `fame_pharmacy_7: 10`, `fame_pharmacy_10: 50`.
pub fn fame_taekwon_mission() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FAME_TAEKWON_MISSION)
}
pub fn fame_refine_lv1() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FAME_REFINE_LV1)
}
pub fn fame_refine_lv2() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FAME_REFINE_LV2)
}
pub fn fame_refine_lv3() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FAME_REFINE_LV3)
}
pub fn fame_forge() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FAME_FORGE)
}
pub fn blacksmith_fame_refine_threshold() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::BLACKSMITH_FAME_REFINE_THRESHOLD)
}
pub fn fame_pharmacy_3() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FAME_PHARMACY_3)
}
pub fn fame_pharmacy_5() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FAME_PHARMACY_5)
}
pub fn fame_pharmacy_7() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FAME_PHARMACY_7)
}
pub fn fame_pharmacy_10() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FAME_PHARMACY_10)
}

/// mmo.hpp struct fame_list.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct FameEntry {
    pub id: i32,
    pub fame: i32,
    pub name: String,
}

/// One fame list: `key` = side * 4 + e_rank (side 0: the char server's list, 1: the map server's copy); always
/// MAX_FAME_LIST entries (id 0 = empty).
#[table(accessor = fame_list)]
#[derive(Clone, Debug)]
pub struct FameList {
    #[primary_key]
    pub key: u8,
    pub entries: Vec<FameEntry>,
}

const CHAR_SIDE: u8 = 0;
const MAP_SIDE: u8 = 4;

fn empty() -> Vec<FameEntry> {
    vec![FameEntry::default(); MAX_FAME_LIST]
}

fn get_list(c: &Ctx, key: u8) -> Vec<FameEntry> {
    ensure(c);
    c.db().fame_list().key().find(key).map(|l| l.entries).unwrap_or_else(empty)
}

fn put_list(c: &Ctx, key: u8, entries: Vec<FameEntry>) {
    if c.db().fame_list().key().find(key).is_some() {
        c.db().fame_list().key().update(FameList { key, entries });
    } else {
        c.db().fame_list().insert(FameList { key, entries });
    }
}

/// The char server starts (char_read_fame_list) and the map server connects (chmapif_send_fame_list(fd) →
/// chrif_recvfamelist): done once, the first time a list is needed.
fn ensure(c: &Ctx) {
    if c.db().fame_list().key().find(CHAR_SIDE + RANK_BLACKSMITH).is_some() {
        return;
    }
    char_read_fame_list(c);
    let lists = send_lists(c);
    chrif_recvfamelist(c, &lists);
}

/// e_rank → (list size, classes) of the char server's lists.
fn rank_info(ranktype: u8) -> Option<(usize, &'static [i32])> {
    match ranktype {
        RANK_BLACKSMITH => Some((FAME_LIST_SIZE_SMITH, &[JOB_BLACKSMITH, JOB_WHITESMITH, JOB_BABY_BLACKSMITH, JOB_MECHANIC, JOB_MECHANIC_T, JOB_BABY_MECHANIC, JOB_MEISTER])),
        RANK_ALCHEMIST => Some((FAME_LIST_SIZE_CHEMIST, &[JOB_ALCHEMIST, JOB_CREATOR, JOB_BABY_ALCHEMIST, JOB_GENETIC, JOB_GENETIC_T, JOB_BABY_GENETIC, JOB_BIOLO])),
        RANK_TAEKWON => Some((FAME_LIST_SIZE_TAEKWON, &[JOB_TAEKWON, JOB_BABY_TAEKWON])),
        _ => None,
    }
}

// ------------------------------------------------------------------------------------------------ char server

/// char.cpp char_read_fame_list: `SELECT char_id, fame, name FROM char WHERE fame > 0 AND class IN (…) ORDER BY
/// fame DESC LIMIT 0, size` for each list. (MariaDB leaves the order of equal fame unspecified; char id order is
/// used for them.)
fn char_read_fame_list(c: &Ctx) {
    for ranktype in [RANK_BLACKSMITH, RANK_ALCHEMIST, RANK_TAEKWON] {
        let Some((size, classes)) = rank_info(ranktype) else { continue };
        let mut rows: Vec<(u32, u32, String)> =
            c.db().character().iter().filter(|ch| ch.fame > 0 && classes.contains(&(ch.class as i32))).map(|ch| (ch.id, ch.fame, ch.name.clone())).collect();
        rows.sort_by(|a, b| b.1.cmp(&a.1).then(a.0.cmp(&b.0)));
        let mut list = empty();
        for (i, (id, fame, name)) in rows.into_iter().take(size).enumerate() {
            list[i] = FameEntry { id: id as i32, fame: fame as i32, name: safe_name(&name) };
        }
        put_list(c, CHAR_SIDE + ranktype, list);
    }
}

use crate::character::character;

/// memcpy(name, data, zmin(len, NAME_LENGTH)).
fn safe_name(s: &str) -> String {
    let b = crate::net::wire::latin1(s);
    crate::net::wire::from_latin1(&b[..b.len().min(24)])
}

/// char.cpp char_loadName: the character's name ("" when there is none: PACKETVER >= 20180221).
fn char_load_name(c: &Ctx, char_id: u32) -> String {
    c.db().character().id().find(char_id).map(|ch| safe_name(&ch.name)).unwrap_or_default()
}

/// char_mapif.cpp chmapif_send_fame_list: the entries of each char list up to its size, stopping at the first
/// empty one (0x2b1b).
fn send_lists(c: &Ctx) -> Vec<Vec<FameEntry>> {
    let mut out = Vec::new();
    for ranktype in [RANK_BLACKSMITH, RANK_ALCHEMIST, RANK_TAEKWON] {
        let (size, _) = rank_info(ranktype).unwrap_or((0, &[]));
        let list = c.db().fame_list().key().find(CHAR_SIDE + ranktype).map(|l| l.entries).unwrap_or_else(empty);
        out.push(list.into_iter().take(size).take_while(|e| e.id != 0).collect());
    }
    out
}

/// char_mapif.cpp chmapif_parse_updfamelist (0x2b10): a character's new fame; the list is updated in place and the
/// map servers get either the one changed value (0x2b22) or the whole lists (0x2b1b).
pub fn chmapif_parse_updfamelist(c: &Ctx, cid: i32, fame: i32, ranktype: u8) {
    let (size, key) = match rank_info(ranktype) {
        Some((size, _)) => (size, CHAR_SIDE + ranktype),
        None => (0, 0),
    };
    if size == 0 {
        return; // not on list and not enough fame to get on it (size 0)
    }
    let mut list = get_list(c, key);
    let player_pos = (0..size).find(|&i| list[i].id == cid).unwrap_or(size); // position of the player
    let mut fame_pos = (0..size).find(|&i| list[i].fame <= fame).unwrap_or(size); // where the player should be
    if player_pos == size && fame_pos == size {
        // not on list and not enough fame to get on it
    } else if fame_pos == player_pos {
        // same position
        list[player_pos].fame = fame;
        put_list(c, key, list);
        intif::send(Inter::FameListAck { ranktype, index: player_pos as u8, fame });
    } else {
        // move in the list
        if player_pos == size {
            // new ranker - not in the list: ARR_MOVE(size - 1, fame_pos) then the entry is overwritten
            let e = list.remove(size - 1);
            list.insert(fame_pos, e);
            let name = char_load_name(c, cid as u32);
            list[fame_pos] = FameEntry { id: cid, fame, name };
        } else {
            // already in the list
            if fame_pos == size {
                fame_pos -= 1; // move to the end of the list
            }
            let e = list.remove(player_pos);
            list.insert(fame_pos, e);
            list[fame_pos].fame = fame;
        }
        put_list(c, key, list);
        intif::send(Inter::FameListRecv { lists: send_lists(c) });
    }
}

/// char_mapif.cpp chmapif_parse_reqfamelist (0x2b1a): the lists are read again and sent to every map server.
pub fn chmapif_parse_reqfamelist(c: &Ctx) {
    ensure(c);
    char_read_fame_list(c);
    intif::send(Inter::FameListRecv { lists: send_lists(c) });
}

// ------------------------------------------------------------------------------------------------ map server

/// chrif.cpp chrif_recvfamelist (0x2b1b): the map server's copies are replaced.
pub fn chrif_recvfamelist(c: &Ctx, lists: &[Vec<FameEntry>]) {
    let mut total = 0;
    for (k, ranktype) in [RANK_BLACKSMITH, RANK_ALCHEMIST, RANK_TAEKWON].into_iter().enumerate() {
        let mut list = empty();
        if let Some(l) = lists.get(k) {
            for (i, e) in l.iter().take(MAX_FAME_LIST).enumerate() {
                list[i] = e.clone();
                total += 1;
            }
        }
        put_list(c, MAP_SIDE + ranktype, list);
    }
    log::info!("Received Fame List of '{total}' characters.");
}

/// chrif.cpp chrif_updatefamelist_ack (0x2b22): one position's fame.
pub fn chrif_updatefamelist_ack(c: &Ctx, ranktype: u8, index: u8, fame: i32) {
    if ranktype > RANK_TAEKWON || index as usize >= MAX_FAME_LIST {
        return;
    }
    let key = MAP_SIDE + ranktype;
    let mut list = get_list(c, key);
    list[index as usize].fame = fame;
    put_list(c, key, list);
}

/// chrif.cpp chrif_updatefamelist (0x2b10).
fn chrif_updatefamelist(c: &Ctx, char_id: u32, fame: i32, ranktype: u8) {
    ensure(c);
    intif::send(Inter::FameUpdate { char_id, fame, ranktype });
}

/// chrif.cpp chrif_buildfamelist (0x2b1a): pc_jobchange asks for new lists after a ranker changed job, or a
/// character with fame became a ranking class.
pub fn chrif_buildfamelist(c: &Ctx) {
    ensure(c);
    intif::send(Inter::FameBuild);
}

/// pc.cpp pc_famerank: the character's place (1..) in the map server's list of `job` (MAPID_BLACKSMITH /
/// MAPID_ALCHEMIST / MAPID_TAEKWON), 0 when not listed.
pub fn pc_famerank(c: &Ctx, char_id: u32, job: u64) -> u8 {
    let ranktype = match job {
        MAPID_BLACKSMITH => RANK_BLACKSMITH,
        MAPID_ALCHEMIST => RANK_ALCHEMIST,
        MAPID_TAEKWON => RANK_TAEKWON,
        _ => return 0,
    };
    let list = get_list(c, MAP_SIDE + ranktype);
    list.iter().position(|e| e.id as u32 == char_id).map(|i| i as u8 + 1).unwrap_or(0)
}

/// sd->class_ (pc_jobid2mapid of the character's class).
fn class_of(c: &Ctx, aid: u32) -> u64 {
    crate::pc::character_of(c, aid).map(|ch| pc_jobid2mapid(ch.class)).unwrap_or(0)
}

/// pc.cpp pc_addfame: fame for a Blacksmith / Alchemist / Taekwon line character (false for any other class).
pub fn pc_addfame(c: &Ctx, aid: u32, count: i32) -> bool {
    let Some(mut ch) = crate::pc::character_of(c, aid) else { return false };
    let ranktype = match pc_jobid2mapid(ch.class) & MAPID_SECONDMASK {
        MAPID_BLACKSMITH => RANK_BLACKSMITH,
        MAPID_ALCHEMIST => RANK_ALCHEMIST,
        MAPID_TAEKWON => RANK_TAEKWON,
        _ => {
            log::warn!("pc_addfame: Trying to add fame to class '{}'({}).", job_name(ch.class as i32), ch.class);
            return false;
        }
    };
    ch.fame = (ch.fame as i32 as i64 + count as i64).clamp(0, MAX_FAME) as u32;
    crate::status::put_char(c, &ch);
    clif_update_rankingpoint(c, aid, ranktype, count as u32);
    chrif_updatefamelist(c, ch.id, ch.fame as i32, ranktype);
    true
}

/// sd->status.fame.
pub fn fame_of(c: &Ctx, aid: u32) -> i32 {
    crate::pc::character_of(c, aid).map(|ch| ch.fame as i32).unwrap_or(0)
}

/// getfamerank: pc_famerank(sd->status.char_id, sd->class_ & MAPID_SECONDMASK).
pub fn famerank_of(c: &Ctx, aid: u32) -> u8 {
    let Some(p) = crate::pc::get(c, aid) else { return 0 };
    pc_famerank(c, p.char_id, class_of(c, aid) & MAPID_SECONDMASK)
}

// ------------------------------------------------------------------------------------------------ clif.cpp

/// clif.cpp clif_ranklist (PACKETVER_RE_NUM >= 20190703: ZC_ACK_RANKING2 0x0af6 with the char ids; RANK_KILLER has
/// no list), mypoints for a ranking class only.
pub fn clif_ranklist(c: &Ctx, aid: u32, rankingtype: u8) {
    let list = match rankingtype {
        RANK_BLACKSMITH | RANK_ALCHEMIST | RANK_TAEKWON => get_list(c, MAP_SIDE + rankingtype),
        RANK_KILLER => Vec::new(), // Currently not supported
        _ => {
            log::error!("clif_sub_ranklist: Unsupported ranking type '{rankingtype}'. Please report this.");
            return;
        }
    };
    let mut w = Vec::with_capacity(88);
    w.extend_from_slice(&0x0af6u16.to_le_bytes());
    w.extend_from_slice(&(rankingtype as u16).to_le_bytes());
    for i in 0..10 {
        w.extend_from_slice(&(list.get(i).map(|e| e.id).unwrap_or(0) as u32).to_le_bytes());
    }
    for i in 0..10 {
        w.extend_from_slice(&(list.get(i).map(|e| e.fame).unwrap_or(0) as u32).to_le_bytes());
    }
    let mypoints = match class_of(c, aid) & MAPID_SECONDMASK {
        MAPID_BLACKSMITH | MAPID_ALCHEMIST | MAPID_TAEKWON => fame_of(c, aid) as u32,
        _ => 0,
    };
    w.extend_from_slice(&mypoints.to_le_bytes());
    if let Some(u) = crate::unit::get(c, aid) {
        crate::clif::send(c, &w, &u, crate::clif::Target::SelfOnly);
    }
}

/// clif.cpp clif_parse_ranklist (CZ_REQ_RANKING 0x097c): /blacksmith, /alchemist, /taekwon, /pk.
pub fn parse_ranklist(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(t) = crate::net::wire::Reader::at(pkt, 2).u16() else { return };
    match t {
        0..=3 => {}
        _ => return,
    }
    clif_ranklist(c, aid, t as u8);
}

/// clif.cpp clif_update_rankingpoint: ZC_UPDATE_RANKING_POINT (0x097e) type.W points.L total.L.
pub fn clif_update_rankingpoint(c: &Ctx, aid: u32, rankingtype: u8, point: u32) {
    let mut w = Vec::with_capacity(12);
    w.extend_from_slice(&0x097eu16.to_le_bytes());
    w.extend_from_slice(&(rankingtype as u16).to_le_bytes());
    w.extend_from_slice(&point.to_le_bytes());
    w.extend_from_slice(&(fame_of(c, aid) as u32).to_le_bytes());
    if let Some(u) = crate::unit::get(c, aid) {
        crate::clif::send(c, &w, &u, crate::clif::Target::SelfOnly);
    }
}
