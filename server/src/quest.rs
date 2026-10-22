//! map/quest.cpp: the character's quest log and everything that changes it (quest_add, quest_change, quest_delete,
//! quest_update_status, quest_update_objective, quest_check), the quest log's load and save through the char server
//! (map/intif.cpp intif_parse_questlog / intif_quest_save, char/int_quest.cpp), clif.cpp's questlog packets, and
//! the NPC quest markers (script.cpp buildin_questinfo's data, pc.cpp pc_show_questinfo / pc_show_questinfo_reinit,
//! map.cpp map_remove_questinfo).
//!
//! State:
//! - `quest`: the char server's `quest` SQL table (char_id, quest_id, state, time, count1..3). It is what a
//!   character logs in with; the map server writes it back whenever rAthena's chrif_save sends the quest log
//!   (save_settings has CHARSAVE_QUEST, so after every add / change / delete / completion, and when the player quits).
//! - `pc_quest`: map_session_data's quest fields while the character is online: sd->quest_log (the first
//!   `avail_quests` entries are not complete, completed ones follow), sd->save_quest and sd->qi_display.
//! - `npc_questinfo` / `map_qi_npc`: nd->qi_data and mapdata->qi_npc, filled by `questinfo` in OnInit.
//!
//! Interfaces for other systems:
//! - login: [`pc_login`] (pc_reg_received → intif_request_questlog → intif_parse_questlog → quest_pc_login);
//!   quit: [`chrif_save`] then [`unit_free`].
//! - monster kills (mob.cpp mob_dead): [`update_objective`] / [`update_objective_sub`] with a [`QuestMob`], after
//!   checking [`avail_quests`].
//! - quest markers: [`pc_show_questinfo`] (pc_additem, pc_delitem, pc_baselevelchanged, pc_checkjoblevelup,
//!   pc_jobchange and quest_update_objective call it in rAthena), [`pc_show_questinfo_reinit`] (LoadEndAck).
//!
//! rAthena keeps no quest timers: a quest's `time` limit is only read by quest_check (PLAYTIME / HUNTING).

use spacetimedb::{table, SpacetimeType, Table};

use crate::db::misc::quest::{quest_search, QuestDb, QuestObjective, MAX_QUEST_OBJECTIVES};
use crate::npc::{npc, npc_function};
use crate::world::Ctx;

/// mmo.hpp e_quest_state.
pub const Q_INACTIVE: u8 = 0;
pub const Q_ACTIVE: u8 = 1;
pub const Q_COMPLETE: u8 = 2;

/// quest.hpp e_quest_check_type.
pub const HAVEQUEST: i32 = 0;
pub const PLAYTIME: i32 = 1;
pub const HUNTING: i32 = 2;

/// script.hpp e_questinfo_types (PACKETVER >= 20170315 names).
pub const QTYPE_QUEST: i32 = 0;
pub const QTYPE_QUEST2: i32 = 1;
pub const QTYPE_JOB: i32 = 2;
pub const QTYPE_JOB2: i32 = 3;
pub const QTYPE_EVENT: i32 = 4;
pub const QTYPE_EVENT2: i32 = 5;
pub const QTYPE_CLICKME: i32 = 6;
pub const QTYPE_DAILYQUEST: i32 = 7;
pub const QTYPE_EVENT3: i32 = 8;
pub const QTYPE_JOBQUEST: i32 = 9;
pub const QTYPE_JUMPING_PORING: i32 = 10;
pub const QTYPE_NONE: i32 = 9999;

/// script.hpp e_questinfo_markcolor.
pub const QMARK_NONE: i32 = 0;
pub const QMARK_YELLOW: i32 = 1;
pub const QMARK_GREEN: i32 = 2;
pub const QMARK_PURPLE: i32 = 3;
pub const QMARK_MAX: i32 = 4;

/// map.hpp save_settings (conf/map_athena.conf `save_settings: 4095`) has CHARSAVE_QUEST (0x040).
const CHARSAVE_QUEST: bool = true;

/// map.hpp e_race RC_ALL, e_size SZ_ALL, e_element ELE_ALL.
const RC_ALL: i8 = 12;
const SZ_ALL: u8 = 3;
const ELE_ALL: i8 = 10;

/// mmo.hpp NAME_LENGTH.
const NAME_LENGTH: usize = 24;

// ------------------------------------------------------------------------------------------------ tables

/// mmo.hpp struct quest: one entry of a character's quest log.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct Quest {
    pub quest_id: i32,
    /// Expiration time (unix seconds; 0 = none).
    pub time: u32,
    /// Kill counters of each objective (MAX_QUEST_OBJECTIVES).
    pub count: Vec<i32>,
    pub state: u8,
}

impl Quest {
    fn zeroed() -> Quest {
        Quest { quest_id: 0, time: 0, count: vec![0; MAX_QUEST_OBJECTIVES], state: Q_INACTIVE }
    }
    fn count(&self, j: usize) -> i32 {
        self.count.get(j).copied().unwrap_or(0)
    }
}

/// pc.hpp struct s_qi_display: what the client was last told about the i-th quest NPC of the map.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct QiDisplay {
    pub is_active: bool,
    pub icon: i32,
    pub color: i32,
}

/// map_session_data's quest fields for an online character.
#[table(accessor = pc_quest)]
#[derive(Clone, Debug)]
pub struct PcQuest {
    #[primary_key]
    pub account_id: u32,
    #[unique]
    pub char_id: u32,
    /// sd->quest_log (num_quests entries): the first `avail_quests` are not complete, completed ones follow.
    pub quest_log: Vec<Quest>,
    pub avail_quests: u32,
    /// sd->save_quest: the log changed since it was last sent to the char server.
    pub save_quest: bool,
    /// sd->qi_display, one per entry of the map's qi_npc (sized by pc_show_questinfo_reinit).
    pub qi_display: Vec<QiDisplay>,
}

/// The char server's `quest` table (sql-files/main.sql: PRIMARY KEY (char_id, quest_id), MyISAM). The map server
/// reads a character's rows with `SELECT ... WHERE char_id=?`, which MariaDB answers through the primary key, so
/// they arrive in quest_id order.
#[table(accessor = quest, index(accessor = by_char, btree(columns = [char_id])))]
#[derive(Clone, Debug)]
pub struct QuestRow {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub char_id: u32,
    pub quest_id: i32,
    pub state: u8,
    pub time: u32,
    pub count1: i32,
    pub count2: i32,
    pub count3: i32,
}

/// npc.hpp struct s_questinfo (nd->qi_data entries, in the order questinfo added them).
#[table(accessor = npc_questinfo, index(accessor = by_npc, btree(columns = [npc_id])))]
#[derive(Clone, Debug)]
pub struct NpcQuestinfo {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub npc_id: u32,
    pub icon: i32,
    pub color: i32,
    /// The condition script's source (already wrapped in `achievement_condition( ... );`), None without one.
    pub condition: Option<String>,
}

/// mapdata->qi_npc: the NPCs of a map that have questinfo, in the order their first questinfo ran.
#[table(accessor = map_qi_npc)]
#[derive(Clone, Debug)]
pub struct MapQiNpc {
    #[primary_key]
    pub map: u16,
    pub npcs: Vec<u32>,
}

// ------------------------------------------------------------------------------------------------ helpers

fn pcq(c: &Ctx, aid: u32) -> Option<PcQuest> {
    c.db().pc_quest().account_id().find(aid)
}

fn put_pcq(c: &Ctx, q: &PcQuest) {
    if c.db().pc_quest().account_id().find(q.account_id).is_some() {
        c.db().pc_quest().account_id().update(q.clone());
    } else {
        c.db().pc_quest().insert(q.clone());
    }
}

/// The player's quest fields; an online character always has them after pc_login (an empty log otherwise).
fn pcq_or_empty(c: &Ctx, aid: u32) -> Option<PcQuest> {
    if let Some(q) = pcq(c, aid) {
        return Some(q);
    }
    let p = crate::pc::get(c, aid)?;
    Some(PcQuest { account_id: aid, char_id: p.char_id, quest_log: Vec::new(), avail_quests: 0, save_quest: false, qi_display: Vec::new() })
}

fn char_id_of(c: &Ctx, aid: u32) -> u32 {
    crate::pc::get(c, aid).map(|p| p.char_id).unwrap_or(0)
}

/// time(nullptr).
fn now(c: &Ctx) -> i64 {
    c.tick().div_euclid(1000)
}

/// WFIFOSET to the player's own connection.
fn send_fd(c: &Ctx, aid: u32, bytes: &[u8]) {
    if let Some(id) = crate::pc::identity_of(c, aid) {
        c.send(id, bytes);
    }
}

/// sd->avail_quests (mob_dead only updates objectives when the killer has quests that are not complete).
pub fn avail_quests(c: &Ctx, aid: u32) -> u32 {
    pcq(c, aid).map(|q| q.avail_quests).unwrap_or(0)
}

/// The character's quest log as the map server holds it (sd->quest_log, sd->avail_quests).
pub fn quest_log(c: &Ctx, aid: u32) -> (Vec<Quest>, u32) {
    pcq(c, aid).map(|q| (q.quest_log, q.avail_quests)).unwrap_or_default()
}

// ------------------------------------------------------------------------------------------------ load / save

/// pc_reg_received → intif_request_questlog → (char server) mapif_parse_quest_load → intif_parse_questlog →
/// quest_pc_login: the character's quest log arrives and the client gets the quest list. The char server is part
/// of this database, so the answer is immediate.
pub fn pc_login(c: &Ctx, aid: u32) {
    let Some(p) = crate::pc::get(c, aid) else { return };
    let char_id = p.char_id;
    // mapif_quests_fromsql: SELECT ... WHERE char_id=? (through the primary key: quest_id order)
    let mut received: Vec<QuestRow> = c.db().quest().by_char().filter(&char_id).collect();
    received.sort_by_key(|r| (r.quest_id as u32, r.id));
    let (log, avail) = intif_parse_questlog(&received, |id| quest_search(id).is_some());
    let q = PcQuest { account_id: aid, char_id, quest_log: log, avail_quests: avail, save_quest: false, qi_display: Vec::new() };
    put_pcq(c, &q);
    quest_pc_login(c, aid);
}

/// intif.cpp intif_parse_questlog: the received rows become sd->quest_log — quests that are not complete in front, in
/// the order received, completed ones from the back (so in reverse order); rows of quests the db does not have are
/// skipped. Returns the log and avail_quests.
fn intif_parse_questlog(received: &[QuestRow], exists: impl Fn(i32) -> bool) -> (Vec<Quest>, u32) {
    let num_received = received.len();
    let mut num_quests = 0usize;
    let mut avail = 0usize;
    // CREATE(sd->quest_log, struct quest, num_received): sd->quest_log is nullptr after pc_setnewpc, so the array
    // starts zeroed
    let mut log = vec![Quest::zeroed(); num_received];
    let mut k = num_received;
    for r in received {
        if !exists(r.quest_id) {
            log::error!("intif_parse_QuestLog: quest {} not found in DB.", r.quest_id);
            continue;
        }
        let q = Quest { quest_id: r.quest_id, time: r.time, count: vec![r.count1, r.count2, r.count3], state: r.state };
        if r.state != Q_COMPLETE {
            // Insert at the beginning
            log[avail] = q;
            avail += 1;
        } else {
            // Insert at the end
            k -= 1;
            log[k] = q;
        }
        num_quests += 1;
    }
    if avail < k {
        // avail_quests and k didn't meet in the middle: some entries were skipped
        if k < num_received {
            // "Move the entries at the end to fill the gap": memmove(&quest_log[k], &quest_log[avail_quests],
            // sizeof(struct quest) * (num_received - k)) — rAthena copies in this direction, so the completed
            // entries are overwritten; kept as it does
            let n = num_received - k;
            let src: Vec<Quest> = log[avail..avail + n].to_vec();
            log[k..k + n].clone_from_slice(&src);
        }
        log.truncate(num_quests);
    }
    (log, avail as u32)
}

/// quest.cpp quest_pc_login.
fn quest_pc_login(c: &Ctx, aid: u32) -> i32 {
    let Some(q) = pcq(c, aid) else { return 1 };
    if q.avail_quests == 0 {
        return 1;
    }
    clif_quest_send_list(c, aid, &q);
    // (PACKETVER < 20141022 only: clif_quest_send_mission and one objective update per quest)
    0
}

/// chrif_save's quest part: `if (sd->save_quest) intif_quest_save(sd)`, answered by the char server's
/// mapif_parse_quest_save (the rows are brought in line with the log: changed states / counts updated, new quests
/// inserted, the others deleted) and its ack (intif_parse_questsave clears save_quest). The rest of chrif_save
/// (the character's status, registries, storages) is written as it changes in this module.
pub fn chrif_save(c: &Ctx, aid: u32) {
    let Some(mut q) = pcq(c, aid) else { return };
    if !q.save_quest {
        return;
    }
    let char_id = q.char_id;
    let mut old: Vec<QuestRow> = c.db().quest().by_char().filter(&char_id).collect();
    old.sort_by_key(|r| (r.quest_id as u32, r.id));
    for n in &q.quest_log {
        match old.iter().position(|o| o.quest_id == n.quest_id) {
            Some(j) => {
                let o = old.remove(j);
                // Only states and counts are changable.
                let counts = [n.count(0), n.count(1), n.count(2)];
                if counts != [o.count1, o.count2, o.count3] || n.state != o.state {
                    c.db().quest().id().update(QuestRow { state: n.state, count1: counts[0], count2: counts[1], count3: counts[2], ..o });
                }
            }
            None => {
                c.db().quest().insert(QuestRow {
                    id: 0,
                    char_id,
                    quest_id: n.quest_id,
                    state: n.state,
                    time: n.time,
                    count1: n.count(0),
                    count2: n.count(1),
                    count3: n.count(2),
                });
            }
        }
    }
    // Quests not in the log but in the table are to be erased.
    for o in old {
        c.db().quest().id().delete(o.id);
    }
    q.save_quest = false;
    put_pcq(c, &q);
}

/// unit_free_pc: sd->quest_log is freed, sd->qi_display cleared.
pub fn unit_free(c: &Ctx, aid: u32) {
    c.db().pc_quest().account_id().delete(aid);
}

/// char.cpp char_delete: `DELETE FROM quest WHERE char_id = ?`.
pub fn char_delete(db: &spacetimedb::Local, char_id: u32) {
    let ids: Vec<u64> = db.quest().by_char().filter(&char_id).map(|r| r.id).collect();
    for id in ids {
        db.quest().id().delete(id);
    }
}

// ------------------------------------------------------------------------------------------------ quest.cpp

/// quest.cpp quest_time: a quest's time limit (`t` = time(nullptr)).
fn quest_time(t: i64, qi: &QuestDb) -> i64 {
    if qi.time < 0 {
        return 0;
    }
    if !qi.time_at && qi.time > 0 {
        return t + qi.time;
    } else if qi.time_at {
        let lt = crate::script::npc_run::localtime(t);
        let time_today = (lt.hour * 3600 + lt.min * 60 + lt.sec) as i64;
        let mut day_shift: i64 = 0;
        if time_today >= qi.time % 86400 {
            // Carry over to the next day
            day_shift = 1;
        }
        if qi.time_week > -1 {
            let wday = lt.wday as i64;
            if (qi.time_week as i64) < wday + day_shift {
                day_shift = qi.time_week as i64 + 7 - wday;
            } else {
                day_shift = qi.time_week as i64 - wday;
            }
        }
        return t + day_shift * 86400 + qi.time - time_today;
    }
    0
}

/// quest.cpp quest_add: a new quest (Q_ACTIVE) goes in front of the completed ones.
pub fn quest_add(c: &Ctx, aid: u32, quest_id: i32) -> i32 {
    let Some(qi) = quest_search(quest_id) else {
        log::error!("quest_add: quest {quest_id} not found in DB.");
        return -1;
    };
    if quest_check(c, aid, quest_id, HAVEQUEST) >= 0 {
        log::error!("quest_add: Character {} already has quest {quest_id}.", char_id_of(c, aid));
        return -1;
    }
    let Some(mut q) = pcq_or_empty(c, aid) else { return -1 };
    let n = q.avail_quests as usize; // Insertion point
    let entry = Quest { quest_id: qi.id, time: quest_time(now(c), qi) as u32, count: vec![0; MAX_QUEST_OBJECTIVES], state: Q_ACTIVE };
    // The character has some completed quests: they stay at the end of the array
    q.quest_log.insert(n.min(q.quest_log.len()), entry.clone());
    q.avail_quests += 1;
    q.save_quest = true;
    put_pcq(c, &q);
    clif_quest_add(c, aid, &entry);
    clif_quest_update_objective(c, aid, &entry);
    if CHARSAVE_QUEST {
        crate::chrif::chrif_save(c, aid, crate::chrif::CSAVE_NORMAL);
    }
    0
}

/// quest.cpp quest_change: replaces an active quest with another one.
pub fn quest_change(c: &Ctx, aid: u32, qid1: i32, qid2: i32) -> i32 {
    let Some(qi) = quest_search(qid2) else {
        log::error!("quest_change: quest {qid2} not found in DB.");
        return -1;
    };
    if quest_check(c, aid, qid2, HAVEQUEST) >= 0 {
        log::error!("quest_change: Character {} already has quest {qid2}.", char_id_of(c, aid));
        return -1;
    }
    if quest_check(c, aid, qid1, HAVEQUEST) < 0 {
        log::error!("quest_change: Character {} doesn't have quest {qid1}.", char_id_of(c, aid));
        return -1;
    }
    let Some(mut q) = pcq(c, aid) else { return -1 };
    let avail = (q.avail_quests as usize).min(q.quest_log.len());
    let Some(i) = q.quest_log[..avail].iter().position(|e| e.quest_id == qid1) else {
        log::error!("quest_change: Character {} has completed quest {qid1}.", q.char_id);
        return -1;
    };
    let entry = Quest { quest_id: qi.id, time: quest_time(now(c), qi) as u32, count: vec![0; MAX_QUEST_OBJECTIVES], state: Q_ACTIVE };
    q.quest_log[i] = entry.clone();
    q.save_quest = true;
    put_pcq(c, &q);
    clif_quest_delete(c, aid, qid1);
    clif_quest_add(c, aid, &entry);
    clif_quest_update_objective(c, aid, &entry);
    if CHARSAVE_QUEST {
        crate::chrif::chrif_save(c, aid, crate::chrif::CSAVE_NORMAL);
    }
    0
}

/// quest.cpp quest_delete: removes a quest from the log (active or completed).
pub fn quest_delete(c: &Ctx, aid: u32, quest_id: i32) -> i32 {
    let q = pcq(c, aid);
    let found = q.as_ref().and_then(|q| q.quest_log.iter().position(|e| e.quest_id == quest_id));
    let (Some(mut q), Some(i)) = (q, found) else {
        log::error!("quest_delete: Character {} doesn't have quest {quest_id}.", char_id_of(c, aid));
        return -1;
    };
    if q.quest_log[i].state != Q_COMPLETE {
        q.avail_quests = q.avail_quests.saturating_sub(1);
    }
    q.quest_log.remove(i); // Compact the array
    q.save_quest = true;
    put_pcq(c, &q);
    clif_quest_delete(c, aid, quest_id);
    if CHARSAVE_QUEST {
        crate::chrif::chrif_save(c, aid, crate::chrif::CSAVE_NORMAL);
    }
    0
}

/// The killed monster as quest_update_objective reads it (mob_data): mob_id, level, status.race, status.size,
/// status.def_ele.
#[derive(Clone, Debug)]
pub struct QuestMob {
    pub mob_id: u16,
    pub level: i32,
    pub race: i8,
    pub size: u8,
    pub def_ele: i8,
}

/// quest.cpp quest_update_objective_sub: map_foreachinallrange(..., AREA_SIZE, BL_PC, party_id, md) for a party
/// kill — one player of the area. Returns 1 when it counted.
pub fn update_objective_sub(c: &Ctx, aid: u32, party_id: i32, md: &QuestMob) -> i32 {
    if avail_quests(c, aid) == 0 {
        return 0;
    }
    let pid = crate::pc::character_of(c, aid).map(|ch| ch.party_id as i32).unwrap_or(0);
    if pid != party_id {
        return 0;
    }
    update_objective(c, aid, md);
    1
}

/// quest.cpp quest_update_objective: a monster the player (or their party) killed counts for the objectives of every
/// quest that is not complete; then the quest's extra drops are rolled, and the NPC markers refreshed.
pub fn update_objective(c: &Ctx, aid: u32, md: &QuestMob) {
    if pcq(c, aid).is_none() {
        return;
    }
    let Some(u) = crate::unit::get(c, aid) else { return };
    let sd_m = u.map as i16;
    let mut i = 0usize;
    // for (i = 0; i < sd->avail_quests; i++) — avail_quests is read again on every pass
    while i < pcq(c, aid).map(|q| q.avail_quests as usize).unwrap_or(0) {
        let idx = i;
        i += 1;
        let Some(entry) = pcq(c, aid).and_then(|q| q.quest_log.get(idx).cloned()) else { break };
        if entry.state == Q_COMPLETE {
            continue; // Skip complete quests
        }
        let Some(qi) = quest_search(entry.quest_id) else { continue };
        // Process quest objectives
        const TOTAL_CHECK: u8 = 7; // Must pass all checks
        for (j, obj) in qi.objectives.iter().enumerate() {
            let mut objective_check: u8 = 0;
            if obj.mob_id == md.mob_id {
                objective_check = TOTAL_CHECK;
            } else if obj.mob_id == 0 {
                if obj.min_level == 0 || obj.min_level as i32 <= md.level {
                    objective_check += 1;
                }
                if obj.max_level == 0 || obj.max_level as i32 >= md.level {
                    objective_check += 1;
                }
                if obj.race == RC_ALL || obj.race == md.race {
                    objective_check += 1;
                }
                if obj.size == SZ_ALL || obj.size == md.size {
                    objective_check += 1;
                }
                if obj.element == ELE_ALL || obj.element == md.def_ele {
                    objective_check += 1;
                }
                if obj.mapid < 0 || obj.mapid == sd_m {
                    objective_check += 1;
                } else if crate::instance_map::instance_src_map(c, sd_m as u16).map(|src| src as i32 == obj.mapid as i32).unwrap_or(false) {
                    // mapdata->instance_id && mapdata->instance_src_map == qi->objectives[j]->mapid
                    objective_check += 1;
                }
                if obj.mobs_allowed.is_empty() || obj.mobs_allowed.contains(&md.mob_id) {
                    objective_check += 1;
                }
            }
            if objective_check != TOTAL_CHECK {
                continue;
            }
            let Some(mut q) = pcq(c, aid) else { return };
            let Some(e) = q.quest_log.get_mut(idx) else { break };
            if e.count(j) < obj.count as i32 {
                if e.count.len() < MAX_QUEST_OBJECTIVES {
                    e.count.resize(MAX_QUEST_OBJECTIVES, 0);
                }
                e.count[j] += 1;
                let e = e.clone();
                q.save_quest = true;
                put_pcq(c, &q);
                clif_quest_update_objective(c, aid, &e);
            }
        }
        // Process quest-granted extra drop bonuses
        for it in &qi.dropitem {
            if it.mob_id != 0 && it.mob_id != md.mob_id {
                continue;
            }
            if it.rate < 10000 && !(c.rnd_value(1, 10000) <= it.rate as i32) {
                continue; // TODO: Should this be affected by server rates?
            }
            if crate::db::items::itemdb_exists(it.nameid).is_none() {
                continue;
            }
            let mut item = crate::inventory::Item::new(it.nameid);
            item.identify = crate::db::items::itemdb_isidentified(it.nameid) as u8;
            item.amount = it.count as _;
            // (BOUND_ITEMS bound, isGUID, isAnnounced: commented out in rAthena too) pc_additem gets amount 1: the
            // entry's Count is copied into the slot and then overwritten by that argument (pc.cpp:6049-6056)
            let result = crate::inventory::pc_additem(c, aid, &mut item, 1, crate::map_log::LOG_TYPE_QUEST);
            if result != crate::inventory::ADDITEM_SUCCESS {
                // Failed to obtain the item
                crate::clif_items::additem(c, aid, 0, 0, result);
            }
        }
    }
    pc_show_questinfo(c, aid);
}

/// quest.cpp quest_update_status: toggles active / inactive, or completes a quest (moved behind the active ones).
pub fn quest_update_status(c: &Ctx, aid: u32, quest_id: i32, status: u8) -> i32 {
    let q = pcq(c, aid);
    let found = q.as_ref().and_then(|q| {
        let avail = (q.avail_quests as usize).min(q.quest_log.len());
        q.quest_log[..avail].iter().position(|e| e.quest_id == quest_id)
    });
    let (Some(mut q), Some(i)) = (q, found) else {
        log::error!("quest_update_status: Character {} doesn't have quest {quest_id}.", char_id_of(c, aid));
        return -1;
    };
    q.quest_log[i].state = status;
    q.save_quest = true;
    if status < Q_COMPLETE {
        put_pcq(c, &q);
        clif_quest_update_status(c, aid, quest_id, status == Q_ACTIVE);
        return 0;
    }
    // The quest is complete, so it needs to be moved to the completed quests block at the end of the array.
    q.avail_quests -= 1;
    let last = q.avail_quests as usize;
    if i < last {
        q.quest_log.swap(i, last);
    }
    put_pcq(c, &q);
    clif_quest_delete(c, aid, quest_id);
    if CHARSAVE_QUEST {
        crate::chrif::chrif_save(c, aid, crate::chrif::CSAVE_NORMAL);
    }
    0
}

/// quest.cpp quest_check. -1 when the character does not have the quest; otherwise
/// HAVEQUEST: its state (an inactive quest answers as active, 1);
/// PLAYTIME: 2 when its time has passed, 1 when complete, else 0;
/// HUNTING: 2 when not complete and every objective is met, 1 when its time has passed, else 0.
pub fn quest_check(c: &Ctx, aid: u32, quest_id: i32, kind: i32) -> i32 {
    let Some(q) = pcq(c, aid) else { return -1 };
    let Some(e) = q.quest_log.iter().find(|e| e.quest_id == quest_id) else { return -1 };
    let t = now(c) as u32;
    match kind {
        HAVEQUEST => {
            if e.state == Q_INACTIVE {
                // Player has the quest but it's in the inactive state; send it as Q_ACTIVE.
                return 1;
            }
            e.state as i32
        }
        PLAYTIME => {
            if e.time < t {
                2
            } else if e.state == Q_COMPLETE {
                1
            } else {
                0
            }
        }
        HUNTING => {
            if e.state == Q_INACTIVE || e.state == Q_ACTIVE {
                // (quest_search cannot fail here: intif_parse_questlog and quest_add only keep quests of the db)
                let objectives: &[QuestObjective] = quest_search(e.quest_id).map(|qi| qi.objectives.as_slice()).unwrap_or(&[]);
                let j = objectives.iter().enumerate().position(|(j, o)| e.count(j) < o.count as i32);
                if j.is_none() {
                    return 2;
                }
                if e.time < t {
                    return 1;
                }
            }
            0
        }
        _ => {
            log::error!("quest_check_quest: Unknown parameter {kind}");
            -1
        }
    }
}

/// clif.cpp clif_parse_questStateAck (CZ_ACTIVE_QUEST 0x02b6): the client toggles a quest.
pub fn parse_quest_state_ack(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = crate::net::packets::PACKET_CZ_ACTIVE_QUEST::read(&mut crate::net::wire::Reader::new(pkt)) else { return };
    quest_update_status(c, aid, p.quest_id as i32, if p.active != 0 { Q_ACTIVE } else { Q_INACTIVE });
}

// ------------------------------------------------------------------------------------------------ clif.cpp

/// clif.cpp clif_quest_len.
fn clif_quest_len(def_len: usize, info_len: usize, avail_quests: usize) -> usize {
    let limit = (0xFFFF - def_len) / info_len;
    avail_quests.min(limit)
}

/// clif.cpp clif_quest_string: "<map>, <race>, <size>, <element>" for an objective without a monster.
fn clif_quest_string(o: &QuestObjective) -> String {
    let race_name = match o.race {
        0 => "Formless",
        1 => "Undead",
        2 => "Brute",
        3 => "Plant",
        4 => "Insect",
        5 => "Fish",
        6 => "Demon",
        7 => "Demihuman",
        8 => "Angel",
        9 => "Dragon",
        RC_ALL => "",
        r => {
            log::warn!("clif_quest_string: Unsupported race {r} - using empty string...");
            ""
        }
    };
    let size_name = match o.size {
        0 => "Small",
        1 => "Medium",
        2 => "Large",
        SZ_ALL => "",
        s => {
            log::warn!("clif_quest_string: Unsupported size {s} - using empty string...");
            ""
        }
    };
    let ele_name = match o.element {
        0 => "Neutral Element",
        1 => "Water Element",
        2 => "Earth Element",
        3 => "Fire Element",
        4 => "Wind Element",
        5 => "Poison Element",
        6 => "Holy Element",
        7 => "Shadow Element",
        8 => "Ghost Element",
        9 => "Undead Element",
        ELE_ALL => "",
        e => {
            log::warn!("clif_quest_string: Unsupported element {e} - using empty string...");
            ""
        }
    };
    let mut s = String::new();
    if !o.map_name.is_empty() {
        s += &o.map_name;
    }
    for part in [race_name, size_name, ele_name] {
        if !part.is_empty() {
            if !s.is_empty() {
                s += ", ";
            }
            s += part;
        }
    }
    s
}

/// The monster an objective shows: `(mob && mob_id > 0) ? mob_id : MOBID_PORING` and its name (the monster's
/// jname, or clif_quest_string), as safestrncpy writes it into NAME_LENGTH.
fn objective_mob(o: &QuestObjective) -> (u32, String) {
    let mob = crate::db::mobs::mob_db().find(o.mob_id as u32);
    match mob {
        Some(m) if o.mob_id > 0 => (o.mob_id as u32, m.jname.clone()),
        _ => (crate::db::mobs::MOBID_PORING, clif_quest_string(o)),
    }
}

/// `race ? race : (size ? size : (element ? element : 0))` (the objective's "mob type").
fn objective_mob_type(o: &QuestObjective) -> u32 {
    if o.race != 0 {
        o.race as i32 as u32
    } else if o.size != 0 {
        o.size as u32
    } else if o.element != 0 {
        o.element as i32 as u32
    } else {
        0
    }
}

/// clif.cpp clif_quest_send_list (PACKETVER >= 20150513): ZC_ALL_QUEST_LIST3 0x09f8 — the quests that are not
/// complete, with their objectives.
fn clif_quest_send_list(c: &Ctx, aid: u32, q: &PcQuest) {
    const SIZE: usize = 22 + NAME_LENGTH;
    let limit = clif_quest_len(8, 15 + SIZE * MAX_QUEST_OBJECTIVES, q.avail_quests as usize).min(q.quest_log.len());
    let mut w: Vec<u8> = Vec::with_capacity(8 + limit * (15 + SIZE * MAX_QUEST_OBJECTIVES));
    w.extend_from_slice(&0x09f8u16.to_le_bytes());
    w.extend_from_slice(&0u16.to_le_bytes());
    w.extend_from_slice(&(limit as u32).to_le_bytes());
    for e in &q.quest_log[..limit] {
        let Some(qi) = quest_search(e.quest_id) else { continue };
        w.extend_from_slice(&(e.quest_id as u32).to_le_bytes());
        w.push(e.state);
        w.extend_from_slice(&((e.time as i64 - qi.time) as u32).to_le_bytes());
        w.extend_from_slice(&e.time.to_le_bytes());
        w.extend_from_slice(&(qi.objectives.len() as u16).to_le_bytes());
        for (j, o) in qi.objectives.iter().enumerate() {
            let (mob_id, name) = objective_mob(o);
            w.extend_from_slice(&((e.quest_id as u32).wrapping_mul(1000).wrapping_add(j as u32)).to_le_bytes());
            w.extend_from_slice(&objective_mob_type(o).to_le_bytes());
            w.extend_from_slice(&mob_id.to_le_bytes());
            w.extend_from_slice(&o.min_level.to_le_bytes());
            w.extend_from_slice(&o.max_level.to_le_bytes());
            w.extend_from_slice(&(e.count(j) as u16).to_le_bytes());
            w.extend_from_slice(&o.count.to_le_bytes());
            crate::net::wire::put_str(&mut w, &name, NAME_LENGTH);
        }
    }
    let len = w.len() as u16;
    w[2..4].copy_from_slice(&len.to_le_bytes());
    send_fd(c, aid, &w);
}

/// clif.cpp clif_quest_add (PACKETVER >= 20150513): ZC_ADD_QUEST_EX 0x09f9, then ZC_HUNTING_QUEST_INFO 0x08fe.
///
/// rAthena writes the "start time" with WFIFOB at offset 7 (one byte) and leaves bytes 8-10 unwritten, as well as
/// the objective slots a quest does not use (the packet always has room for MAX_QUEST_OBJECTIVES): those bytes are
/// whatever the send buffer held there before. Here they are zero.
fn clif_quest_add(c: &Ctx, aid: u32, qd: &Quest) {
    let Some(qi) = quest_search(qd.quest_id) else { return };
    let plen = crate::net::packets::packet_len(0x09f9).unwrap_or(143) as usize;
    let mut w = vec![0u8; plen];
    w[0..2].copy_from_slice(&0x09f9u16.to_le_bytes());
    w[2..6].copy_from_slice(&(qd.quest_id as u32).to_le_bytes());
    w[6] = qd.state;
    w[7] = (qd.time as i64 - qi.time) as u8;
    w[11..15].copy_from_slice(&qd.time.to_le_bytes());
    w[15..17].copy_from_slice(&(qi.objectives.len() as u16).to_le_bytes());
    let mut offset = 17usize;
    for (i, o) in qi.objectives.iter().enumerate() {
        let (mob_id, name) = objective_mob(o);
        let mut sub = Vec::with_capacity(42);
        sub.extend_from_slice(&((qd.quest_id as u32).wrapping_mul(1000).wrapping_add(i as u32)).to_le_bytes());
        sub.extend_from_slice(&objective_mob_type(o).to_le_bytes()); // effect ?
        // 0 can't be used as it displays "Novice" job regardless of the clif_mobtype_name
        sub.extend_from_slice(&mob_id.to_le_bytes());
        sub.extend_from_slice(&o.min_level.to_le_bytes());
        sub.extend_from_slice(&o.max_level.to_le_bytes());
        sub.extend_from_slice(&(qd.count(i) as u16).to_le_bytes());
        crate::net::wire::put_str(&mut sub, &name, NAME_LENGTH);
        let end = (offset + sub.len()).min(plen);
        w[offset..end].copy_from_slice(&sub[..end - offset]);
        offset += sub.len();
    }
    send_fd(c, aid, &w);

    let len = 4 + qi.objectives.len() * 12;
    let mut h = Vec::with_capacity(len);
    h.extend_from_slice(&0x08feu16.to_le_bytes());
    h.extend_from_slice(&(len as u16).to_le_bytes());
    for (i, o) in qi.objectives.iter().enumerate() {
        h.extend_from_slice(&((qd.quest_id as u32).wrapping_mul(1000).wrapping_add(i as u32)).to_le_bytes());
        h.extend_from_slice(&(o.mob_id as u32).to_le_bytes());
        h.extend_from_slice(&o.count.to_le_bytes());
        h.extend_from_slice(&(qd.count(i) as u16).to_le_bytes());
    }
    send_fd(c, aid, &h);
}

/// clif.cpp clif_quest_delete: ZC_DEL_QUEST 0x02b4.
pub(crate) fn clif_quest_delete(c: &Ctx, aid: u32, quest_id: i32) {
    let mut w = Vec::with_capacity(6);
    w.extend_from_slice(&0x02b4u16.to_le_bytes());
    w.extend_from_slice(&(quest_id as u32).to_le_bytes());
    send_fd(c, aid, &w);
}

/// clif.cpp clif_quest_update_objective (PACKETVER >= 20150513): ZC_UPDATE_MISSION_HUNT_EX 0x09fa.
fn clif_quest_update_objective(c: &Ctx, aid: u32, qd: &Quest) {
    let Some(qi) = quest_search(qd.quest_id) else { return };
    let mut w = Vec::with_capacity(6 + qi.objectives.len() * 12);
    w.extend_from_slice(&0x09fau16.to_le_bytes());
    w.extend_from_slice(&0u16.to_le_bytes());
    w.extend_from_slice(&(qi.objectives.len() as u16).to_le_bytes());
    for (i, o) in qi.objectives.iter().enumerate() {
        w.extend_from_slice(&(qd.quest_id as u32).to_le_bytes());
        w.extend_from_slice(&((qd.quest_id as u32).wrapping_mul(1000).wrapping_add(i as u32)).to_le_bytes());
        w.extend_from_slice(&o.count.to_le_bytes());
        w.extend_from_slice(&(qd.count(i) as u16).to_le_bytes());
    }
    let len = w.len() as u16;
    w[2..4].copy_from_slice(&len.to_le_bytes());
    send_fd(c, aid, &w);
}

/// clif.cpp clif_quest_update_status: ZC_ACTIVE_QUEST 0x02b7.
fn clif_quest_update_status(c: &Ctx, aid: u32, quest_id: i32, active: bool) {
    let mut w = Vec::with_capacity(7);
    w.extend_from_slice(&0x02b7u16.to_le_bytes());
    w.extend_from_slice(&(quest_id as u32).to_le_bytes());
    w.push(active as u8);
    send_fd(c, aid, &w);
}

/// clif.cpp clif_quest_show_event: ZC_QUEST_NOTIFY_EFFECT 0x0446 — an NPC's quest marker (effect QTYPE_*, color
/// QMARK_*) at the NPC's position.
pub fn clif_quest_show_event(c: &Ctx, aid: u32, npc_id: u32, effect: i32, color: i32) {
    let (x, y) = match crate::unit::get(c, npc_id) {
        Some(u) => (u.x, u.y),
        None => match c.db().npc().id().find(npc_id) {
            Some(nd) => (nd.x, nd.y),
            None => return,
        },
    };
    let mut w = Vec::with_capacity(14);
    w.extend_from_slice(&0x0446u16.to_le_bytes());
    w.extend_from_slice(&npc_id.to_le_bytes());
    w.extend_from_slice(&(x as u16).to_le_bytes());
    w.extend_from_slice(&(y as u16).to_le_bytes());
    w.extend_from_slice(&(effect as u16).to_le_bytes());
    w.extend_from_slice(&(color as u16).to_le_bytes());
    send_fd(c, aid, &w);
}

/// clif.cpp clif_ui_open(sd, OUT_UI_QUEST, data): ZC_UI_OPEN 0x0ae2 (the quest UI needs no state tracking).
pub fn clif_ui_open_quest(c: &Ctx, aid: u32, data: i32) {
    const OUT_UI_QUEST: i8 = 6;
    let p = crate::net::packets::PACKET_ZC_UI_OPEN { PacketType: crate::net::packets::PACKET_ZC_UI_OPEN::ID as i16, UIType: OUT_UI_QUEST, data };
    send_fd(c, aid, &p.to_vec());
}

// ------------------------------------------------------------------------------------------------ quest markers

/// The NPC ids of a map's qi_npc.
pub fn map_qi_npc(c: &Ctx, m: u16) -> Vec<u32> {
    c.db().map_qi_npc().map().find(m).map(|r| r.npcs).unwrap_or_default()
}

/// nd->qi_data, in order.
fn npc_qi_data(c: &Ctx, npc_id: u32) -> Vec<NpcQuestinfo> {
    let mut v: Vec<NpcQuestinfo> = c.db().npc_questinfo().by_npc().filter(&npc_id).collect();
    v.sort_by_key(|r| r.id);
    v
}

/// buildin_questinfo's data part: `nd->qi_data.push_back(qi)` and, the first time, the NPC joins its map's qi_npc.
pub fn add_questinfo(c: &Ctx, npc_id: u32, m: u16, icon: i32, color: i32, condition: Option<String>) {
    c.db().npc_questinfo().insert(NpcQuestinfo { id: 0, npc_id, icon, color, condition });
    match c.db().map_qi_npc().map().find(m) {
        Some(mut r) => {
            if !r.npcs.contains(&npc_id) {
                r.npcs.push(npc_id);
                c.db().map_qi_npc().map().update(r);
            }
        }
        None => {
            c.db().map_qi_npc().insert(MapQiNpc { map: m, npcs: vec![npc_id] });
        }
    }
}

/// map.cpp map_remove_questinfo (npc_unload with single): the NPC leaves its map's qi_npc and loses its qi_data.
pub fn map_remove_questinfo(c: &Ctx, npc_id: u32, m: i32) {
    if m >= 0 {
        if let Some(mut r) = c.db().map_qi_npc().map().find(m as u16) {
            if let Some(i) = r.npcs.iter().position(|&n| n == npc_id) {
                r.npcs.remove(i);
                c.db().map_qi_npc().map().update(r);
            }
        }
    }
    let ids: Vec<u64> = c.db().npc_questinfo().by_npc().filter(&npc_id).map(|r| r.id).collect();
    for id in ids {
        c.db().npc_questinfo().id().delete(id);
    }
}

/// The world is built again (a map-server start): no NPC has questinfo until OnInit runs.
pub fn clear_questinfo(c: &Ctx) {
    for id in c.db().npc_questinfo().iter().map(|r| r.id).collect::<Vec<_>>() {
        c.db().npc_questinfo().id().delete(id);
    }
    for m in c.db().map_qi_npc().iter().map(|r| r.map).collect::<Vec<_>>() {
        c.db().map_qi_npc().map().delete(m);
    }
}

/// pc.cpp pc_show_questinfo_reinit: the player's marker state is sized for the map they are on.
pub fn pc_show_questinfo_reinit(c: &Ctx, aid: u32) {
    let Some(mut q) = pcq_or_empty(c, aid) else { return };
    q.qi_display.clear();
    if let Some(u) = crate::unit::get(c, aid) {
        let n = map_qi_npc(c, u.map).len();
        q.qi_display = vec![QiDisplay::default(); n];
    }
    put_pcq(c, &q);
}

/// pc.cpp pc_show_questinfo: for each quest NPC of the map, the first questinfo whose condition holds for the player
/// is shown (when it differs from what the client shows), or the marker is removed when none holds.
pub fn pc_show_questinfo(c: &Ctx, aid: u32) {
    let Some(u) = crate::unit::get(c, aid) else { return };
    let m = u.map;
    let qi_npc = map_qi_npc(c, m);
    if qi_npc.is_empty() {
        return;
    }
    let Some(q) = pcq(c, aid) else { return };
    if qi_npc.len() != q.qi_display.len() {
        return; // init was not called yet
    }
    for (i, &npc_id) in qi_npc.iter().enumerate() {
        if c.db().npc().id().find(npc_id).is_none() {
            continue;
        }
        let qi_data = npc_qi_data(c, npc_id);
        if qi_data.is_empty() {
            continue;
        }
        let mut show = false;
        for qi in &qi_data {
            let ok = match &qi.condition {
                None => true,
                Some(cond) => crate::achievement::achievement_check_condition(c, Some(&condition_key(cond)), aid),
            };
            if ok {
                show = true;
                // Check if need to be displayed
                let Some(mut q) = pcq(c, aid) else { return };
                let Some(d) = q.qi_display.get_mut(i) else { return };
                if !d.is_active || qi.icon != d.icon || qi.color != d.color {
                    d.is_active = true;
                    d.icon = qi.icon;
                    d.color = qi.color;
                    put_pcq(c, &q);
                    clif_quest_show_event(c, aid, npc_id, qi.icon, qi.color);
                }
                break;
            }
        }
        if !show {
            // Check if need to be hide
            let Some(mut q) = pcq(c, aid) else { return };
            let Some(d) = q.qi_display.get_mut(i) else { return };
            if d.is_active {
                d.is_active = false;
                d.icon = QTYPE_NONE;
                d.color = QMARK_NONE;
                put_pcq(c, &q);
                // PACKETVER >= 20120410
                clif_quest_show_event(c, aid, npc_id, QTYPE_NONE, QMARK_NONE);
            }
        }
    }
}

// ------------------------------------------------------------------------------------------------ conditions

/// The source kind questinfo conditions compile under (script::code providers).
pub const QUESTINFO_KIND: &str = "questinfo";

thread_local! {
    static PROVIDER: std::cell::Cell<bool> = const { std::cell::Cell::new(false) };
}

/// parse_script(condition, "questinfoparsing", 0, SCRIPT_IGNORE_EXTERNAL_BRACKETS): the condition text is its own
/// key. questinfo runs in OnInit, after every NPC file was loaded, so every user function exists.
fn provider(c: Option<&Ctx>, key: &str) -> Option<crate::script::code::ExtSource> {
    let userfuncs = c.map(|c| c.db().npc_function().iter().map(|f| f.name).collect()).unwrap_or_default();
    Some(crate::script::code::ExtSource {
        text: crate::net::wire::latin1(key),
        file: "questinfoparsing".into(),
        line: 0,
        options: crate::script::compiler::SCRIPT_IGNORE_EXTERNAL_BRACKETS,
        userfuncs,
    })
}

/// The code key of a condition (registers the provider in this module instance the first time).
pub fn condition_key(cond: &str) -> crate::script::vm::CodeKey {
    if !PROVIDER.with(|p| p.get()) {
        crate::script::code::register_provider(QUESTINFO_KIND, provider);
        PROVIDER.with(|p| p.set(true));
    }
    crate::script::vm::CodeKey::Ext(crate::script::vm::ExtCode { kind: QUESTINFO_KIND.into(), key: cond.to_string() })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn row(quest_id: i32, state: u8) -> QuestRow {
        QuestRow { id: 0, char_id: 1, quest_id, state, time: 0, count1: 0, count2: 0, count3: 0 }
    }

    #[test]
    fn questlog_order() {
        // active quests in the order received, completed ones reversed at the end
        let rows = [row(1, Q_COMPLETE), row(2, Q_ACTIVE), row(3, Q_COMPLETE), row(4, Q_INACTIVE)];
        let (log, avail) = intif_parse_questlog(&rows, |_| true);
        assert_eq!(avail, 2);
        assert_eq!(log.iter().map(|q| q.quest_id).collect::<Vec<_>>(), vec![2, 4, 3, 1]);
        // a skipped row: rAthena's memmove copies the (zeroed) gap over the completed entries
        let rows = [row(1, Q_COMPLETE), row(2, Q_ACTIVE), row(9, Q_ACTIVE)];
        let (log, avail) = intif_parse_questlog(&rows, |id| id != 9);
        assert_eq!(avail, 1);
        assert_eq!(log.iter().map(|q| q.quest_id).collect::<Vec<_>>(), vec![2, 0]);
    }

    #[test]
    fn objective_strings() {
        let mut o = QuestObjective { race: RC_ALL, size: SZ_ALL, element: ELE_ALL, map_name: "pay_d03_i".into(), ..Default::default() };
        assert_eq!(clif_quest_string(&o), "pay_d03_i");
        assert_eq!(objective_mob_type(&o), 12);
        o.race = 7;
        o.element = 3;
        assert_eq!(clif_quest_string(&o), "pay_d03_i, Demihuman, Fire Element");
        o.map_name.clear();
        o.size = 2;
        assert_eq!(clif_quest_string(&o), "Demihuman, Large, Fire Element");
        o.race = 0; // RC_FORMLESS is 0, so the size decides
        assert_eq!(objective_mob_type(&o), 2);
        o.size = 0; // SZ_SMALL is 0 too: the element
        assert_eq!(objective_mob_type(&o), 3);
    }

    #[test]
    fn time_limits() {
        // 2026-09-27 03:59:50 at UTC-3 (a Sunday)
        let t = 1_790_492_390 - 3600 - 10 + 3600;
        let lt = crate::script::npc_run::localtime(t);
        assert_eq!((lt.hour, lt.min, lt.sec, lt.wday), (3, 59, 40, 0));
        let at4 = QuestDb { time: 4 * 3600, time_at: true, time_week: -1, ..Default::default() };
        assert_eq!(quest_time(t, &at4), 1_790_492_400); // today 04:00
        assert_eq!(quest_time(t + 30, &at4), 1_790_492_400 + 86_400); // past 04:00: tomorrow
        let plus = QuestDb { time: 3 * 3600, time_at: false, time_week: 0, ..Default::default() };
        assert_eq!(quest_time(t, &plus), t + 3 * 3600);
        let monday = QuestDb { time: 4 * 3600, time_at: true, time_week: 1, ..Default::default() };
        assert_eq!(quest_time(t, &monday), 1_790_492_400 + 86_400); // Monday 04:00
        assert_eq!(quest_time(t, &QuestDb::default()), 0);
    }
}
