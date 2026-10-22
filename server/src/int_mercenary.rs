//! char/int_mercenary.cpp: the char server's `mercenary`, `mercenary_owner` and `skillcooldown_mercenary` tables and
//! its answers to the map server's mercenary requests (inter link 0x3070-0x3073 → 0x3870-0x3872): creating a
//! mercenary (mapif_parse_mercenary_create), loading one (mapif_parse_mercenary_load), deleting one
//! (mapif_parse_mercenary_delete) and saving one (mapif_parse_mercenary_save); and char.cpp's parts that read and write
//! the owner's mercenary guild counters (mercenary_owner_fromsql / _tosql / _delete). What travels on the link is
//! [`SMercenary`] (mmo.hpp struct s_mercenary). The messages go through the inter-link queue (intif.rs), so every
//! answer comes after the map-server function that asked, as on rAthena.

use spacetimedb::{table, SpacetimeType, Table};

use crate::companion::MercElemInter;
use crate::intif::{self, Inter};
use crate::world::Ctx;

/// mmo.hpp MAX_SKILLCOOLDOWN.
pub const MAX_SKILLCOOLDOWN: usize = 20;
/// mmo.hpp MC_SKILLBASE / MAX_MERCSKILL.
pub const MC_SKILLBASE: u16 = 8201;
pub const MAX_MERCSKILL: u16 = 41;

/// mmo.hpp struct s_skill_cooldown_data.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct SkillCooldownData {
    pub skill_id: u16,
    pub tick: i64,
}

/// mmo.hpp struct s_mercenary: a mercenary as the map server keeps it (`md->mercenary`) and as the inter link carries
/// it. `scd` always holds MAX_SKILLCOOLDOWN slots (the C array; empty slots are skill 0).
#[derive(SpacetimeType, Clone, Debug, PartialEq)]
pub struct SMercenary {
    pub mercenary_id: i32,
    pub char_id: u32,
    pub class_: i16,
    pub hp: i32,
    pub sp: i32,
    pub kill_count: u32,
    pub life_time: i64,
    pub scd: Vec<SkillCooldownData>,
}

impl Default for SMercenary {
    /// `s_mercenary merc = {}` / memset(merc, 0, …).
    fn default() -> Self {
        SMercenary { mercenary_id: 0, char_id: 0, class_: 0, hp: 0, sp: 0, kill_count: 0, life_time: 0, scd: vec![SkillCooldownData::default(); MAX_SKILLCOOLDOWN] }
    }
}

/// The SQL `mercenary` table (sql-files/main.sql). `mer_id` is the table's AUTO_INCREMENT (ids.rs, kind
/// "mercenary", from 1).
#[table(accessor = mercenary, index(accessor = by_char, btree(columns = [char_id])))]
#[derive(Clone, Debug)]
pub struct MercenaryRow {
    #[primary_key]
    pub mer_id: u32,
    pub char_id: u32,
    pub class: i16,
    pub hp: i32,
    pub sp: i32,
    pub kill_counter: u32,
    pub life_time: i64,
}

/// The SQL `mercenary_owner` table: a character's current mercenary and its three guilds' call and faith counters
/// (mmo_charstatus mer_id, arch_calls … sword_faith). A character that never had a mercenary has no row.
#[table(accessor = mercenary_owner)]
#[derive(Clone, Debug, Default, PartialEq)]
pub struct MercenaryOwner {
    #[primary_key]
    pub char_id: u32,
    pub merc_id: i32,
    pub arch_calls: i32,
    pub arch_faith: i32,
    pub spear_calls: i32,
    pub spear_faith: i32,
    pub sword_calls: i32,
    pub sword_faith: i32,
}

/// The SQL `skillcooldown_mercenary` table: (mer_id, skill, tick). Rows come back in insertion order (the auto-inc
/// id), as skill/cooldown.rs keeps the players' `skillcooldown` table.
#[table(accessor = skillcooldown_mercenary, index(accessor = by_mer, btree(columns = [mer_id])))]
#[derive(Clone, Debug)]
pub struct SkillCooldownMercenary {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub mer_id: i32,
    pub skill: u16,
    pub tick: i64,
}

// ---------------------------------------------------------------------------------------------- mercenary_owner

/// int_mercenary.cpp mercenary_owner_fromsql (char.cpp char_mmo_char_fromsql): the owner's counters, or None when the
/// character has no row (the mmo_charstatus fields stay 0).
pub fn mercenary_owner_fromsql(c: &Ctx, char_id: u32) -> Option<MercenaryOwner> {
    c.db().mercenary_owner().char_id().find(char_id)
}

/// int_mercenary.cpp mercenary_owner_tosql: `REPLACE INTO mercenary_owner` (char.cpp char_mmo_char_tosql calls it when
/// one of the fields differs from the char server's copy).
pub fn mercenary_owner_tosql(c: &Ctx, o: &MercenaryOwner) -> bool {
    match c.db().mercenary_owner().char_id().find(o.char_id) {
        Some(old) if old == *o => {}
        Some(_) => {
            c.db().mercenary_owner().char_id().update(o.clone());
        }
        None => {
            c.db().mercenary_owner().insert(o.clone());
        }
    }
    true
}

/// int_mercenary.cpp mercenary_owner_delete (char.cpp char_delete): the owner's mercenary cooldowns, owner row and
/// mercenaries go.
pub fn mercenary_owner_delete(db: &spacetimedb::Local, char_id: u32) -> bool {
    // DELETE FROM skillcooldown_mercenary WHERE mer_id IN (SELECT merc_id FROM mercenary_owner WHERE char_id = …)
    if let Some(o) = db.mercenary_owner().char_id().find(char_id) {
        let ids: Vec<u64> = db.skillcooldown_mercenary().by_mer().filter(o.merc_id).map(|r| r.id).collect();
        for id in ids {
            db.skillcooldown_mercenary().id().delete(id);
        }
    }
    db.mercenary_owner().char_id().delete(char_id);
    let mers: Vec<u32> = db.mercenary().by_char().filter(char_id).map(|r| r.mer_id).collect();
    for id in mers {
        db.mercenary().mer_id().delete(id);
    }
    true
}

// ---------------------------------------------------------------------------------------------- mapif

/// int_mercenary.cpp mapif_mercenary_save: a new mercenary (id 0) is INSERTed and gets its id; an existing one is
/// UPDATEd (an UPDATE of a missing row changes nothing). Then the skill cooldowns are replaced.
pub fn mapif_mercenary_save(c: &Ctx, merc: &mut SMercenary) -> bool {
    if merc.mercenary_id == 0 {
        // Create new DB entry
        let id = crate::ids::next(c.rc, "mercenary", 1);
        c.db().mercenary().insert(MercenaryRow { mer_id: id, char_id: merc.char_id, class: merc.class_, hp: merc.hp, sp: merc.sp, kill_counter: merc.kill_count, life_time: merc.life_time });
        merc.mercenary_id = id as i32;
    } else if let Some(mut r) = c.db().mercenary().mer_id().find(merc.mercenary_id as u32) {
        // Update DB entry
        r.char_id = merc.char_id;
        r.class = merc.class_;
        r.hp = merc.hp;
        r.sp = merc.sp;
        r.kill_counter = merc.kill_count;
        r.life_time = merc.life_time;
        c.db().mercenary().mer_id().update(r);
    }
    // Save skill cooldowns
    let old: Vec<u64> = c.db().skillcooldown_mercenary().by_mer().filter(merc.mercenary_id).map(|r| r.id).collect();
    for id in old {
        c.db().skillcooldown_mercenary().id().delete(id);
    }
    for e in merc.scd.iter().take(MAX_SKILLCOOLDOWN) {
        if e.skill_id == 0 || e.tick == 0 {
            continue;
        }
        c.db().skillcooldown_mercenary().insert(SkillCooldownMercenary { id: 0, mer_id: merc.mercenary_id, skill: e.skill_id, tick: e.tick });
    }
    true
}

/// int_mercenary.cpp mapif_mercenary_load: the row of `merc_id` owned by `char_id` and its skill cooldowns (at most
/// MAX_SKILLCOOLDOWN, mercenary skills only). A missing row: false, with the zeroed s_mercenary keeping only the ids.
pub fn mapif_mercenary_load(c: &Ctx, merc_id: i32, char_id: u32) -> (bool, SMercenary) {
    let mut merc = SMercenary { mercenary_id: merc_id, char_id, ..Default::default() };
    let Some(r) = c.db().mercenary().mer_id().find(merc_id as u32).filter(|r| r.char_id == char_id) else {
        return (false, merc);
    };
    merc.class_ = r.class;
    merc.hp = r.hp;
    merc.sp = r.sp;
    merc.kill_count = r.kill_counter;
    merc.life_time = r.life_time;
    // (charserv_config.save_log) "Mercenary loaded (ID: %d / Class: %d / CID: %d)."
    log::info!("Mercenary loaded (ID: {} / Class: {} / CID: {}).", merc.mercenary_id, merc.class_, merc.char_id);
    // Load Mercenary Skill Cooldown
    let mut rows: Vec<SkillCooldownMercenary> = c.db().skillcooldown_mercenary().by_mer().filter(merc_id).collect();
    rows.sort_by_key(|r| r.id);
    let mut count = 0usize;
    for r in rows {
        if count == MAX_SKILLCOOLDOWN {
            log::warn!("Too many skillcooldowns for mercenary {merc_id}, skipping.");
            break;
        }
        if r.skill < MC_SKILLBASE || r.skill >= MC_SKILLBASE + MAX_MERCSKILL {
            continue; // invalid skill ID
        }
        merc.scd[count] = SkillCooldownData { skill_id: r.skill, tick: r.tick };
        count += 1;
    }
    (true, merc)
}

/// int_mercenary.cpp mapif_mercenary_delete: the cooldowns and the row go.
pub fn mapif_mercenary_delete(c: &Ctx, merc_id: i32) -> bool {
    let ids: Vec<u64> = c.db().skillcooldown_mercenary().by_mer().filter(merc_id).map(|r| r.id).collect();
    for id in ids {
        c.db().skillcooldown_mercenary().id().delete(id);
    }
    c.db().mercenary().mer_id().delete(merc_id as u32);
    true
}

/// int_mercenary.cpp mapif_parse_mercenary_create (0x3070): save, then mapif_mercenary_send (0x3870).
pub fn mapif_parse_mercenary_create(c: &Ctx, mut merc: SMercenary) {
    let result = mapif_mercenary_save(c, &mut merc);
    intif::send(Inter::MercElem(MercElemInter::MercenaryReceived { merc, flag: result }));
}

/// int_mercenary.cpp mapif_parse_mercenary_load (0x3071): load, then mapif_mercenary_send (0x3870).
pub fn mapif_parse_mercenary_load(c: &Ctx, merc_id: i32, char_id: u32) {
    let (result, merc) = mapif_mercenary_load(c, merc_id, char_id);
    intif::send(Inter::MercElem(MercElemInter::MercenaryReceived { merc, flag: result }));
}

/// int_mercenary.cpp mapif_parse_mercenary_delete (0x3072): delete, then mapif_mercenary_deleted (0x3871).
pub fn mapif_parse_mercenary_delete(c: &Ctx, merc_id: i32) {
    let result = mapif_mercenary_delete(c, merc_id);
    intif::send(Inter::MercElem(MercElemInter::MercenaryDeleted { flag: result }));
}

/// int_mercenary.cpp mapif_parse_mercenary_save (0x3073): save, then mapif_mercenary_saved (0x3872).
pub fn mapif_parse_mercenary_save(c: &Ctx, mut merc: SMercenary) {
    let result = mapif_mercenary_save(c, &mut merc);
    intif::send(Inter::MercElem(MercElemInter::MercenarySaved { flag: result }));
}
