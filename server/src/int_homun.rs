//! char/int_homun.cpp: the char server's homunculus tables and its answers to the map server's homunculus requests
//! (inter link 0x3090-0x3093 → 0x3890-0x3893): creating a homunculus (mapif_parse_homunculus_create), loading one
//! (mapif_parse_homunculus_load), saving one (mapif_parse_homunculus_save) and deleting one
//! (mapif_parse_homunculus_delete); and map/intif.cpp's side of the same link (intif_homunculus_* requests,
//! intif_parse_*Homunculus* answers). The SQL rows are the `homunculus`, `skill_homunculus` and
//! `skillcooldown_homunculus` tables; what the map server holds of a homunculus (`hd->homunculus`) and what travels on
//! the link is [`SHomunculus`] (mmo.hpp struct s_homunculus). The messages go through the inter-link queue (intif.rs,
//! `Inter::Homun`), so every answer comes after the map-server function that asked, as on rAthena.
//! (0x3094 mapif_parse_homunculus_rename is never sent by the map server: renames go through intif_rename, 0x3006,
//! type 2 — int_pet.rs mapif_parse_name_change_request.)

use spacetimedb::{table, SpacetimeType, Table};

use crate::intif::{self, Inter};
use crate::world::Ctx;

/// mmo.hpp MAX_HOMUNSKILL / HM_SKILLBASE.
pub const MAX_HOMUNSKILL: usize = 59;
pub const HM_SKILLBASE: u16 = 8001;
/// mmo.hpp MAX_SKILLCOOLDOWN.
pub const MAX_SKILLCOOLDOWN: usize = 20;

/// The SQL `homunculus` table (sql-files/main.sql, MyISAM). `homun_id` is the table's AUTO_INCREMENT (ids.rs, kind
/// "homunculus"): a counter that starts at 1 and never hands out an id twice. Column types follow what
/// mapif_homunculus_save writes (the struct's values; `alive` is never written by rAthena and keeps its default 1).
#[table(accessor = homunculus)]
#[derive(Clone, Debug)]
pub struct HomunculusRow {
    #[primary_key]
    pub homun_id: u32,
    #[index(btree)]
    pub char_id: u32,
    pub class_: i16,
    pub prev_class: i16,
    pub name: String,
    pub level: i16,
    pub exp: u64,
    pub intimacy: u32,
    pub hunger: i16,
    pub str: i32,
    pub agi: i32,
    pub vit: i32,
    pub int_: i32,
    pub dex: i32,
    pub luk: i32,
    pub hp: u32,
    pub max_hp: u32,
    pub sp: u32,
    pub max_sp: u32,
    pub skill_point: i16,
    pub alive: i8,
    pub rename_flag: i16,
    pub vaporize: i16,
    pub autofeed: bool,
}

/// The SQL `skill_homunculus` table: PRIMARY KEY (homun_id, id); the rows of one homunculus are read in key order.
#[table(accessor = skill_homunculus, index(accessor = by_homun, btree(columns = [homun_id])))]
#[derive(Clone, Debug)]
pub struct SkillHomunculusRow {
    #[primary_key]
    #[auto_inc]
    pub row_id: u64,
    pub homun_id: u32,
    pub id: u16,
    pub lv: u16,
}

/// The SQL `skillcooldown_homunculus` table: PRIMARY KEY (homun_id, skill); read in key order (skill id).
#[table(accessor = skillcooldown_homunculus, index(accessor = by_homun, btree(columns = [homun_id])))]
#[derive(Clone, Debug)]
pub struct SkillcooldownHomunculusRow {
    #[primary_key]
    #[auto_inc]
    pub row_id: u64,
    pub homun_id: u32,
    pub skill: u16,
    pub tick: i64,
}

/// mmo.hpp struct s_skill (id, lv, flag: e_skill_flag).
#[derive(SpacetimeType, Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct HSkill {
    pub id: u16,
    pub lv: u8,
    pub flag: u8,
}

/// mmo.hpp struct s_skill_cooldown_data.
#[derive(SpacetimeType, Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct HomScd {
    pub skill_id: u16,
    pub tick: i64,
}

/// mmo.hpp struct s_homunculus: a homunculus as the map server keeps it (`hd->homunculus`) and as the inter link
/// carries it. `hskill` always has MAX_HOMUNSKILL entries (index = skill id - HM_SKILLBASE), `scd` MAX_SKILLCOOLDOWN.
/// `hom_id` is int32 in rAthena; ids are positive, 0 = new / none.
#[derive(SpacetimeType, Clone, Debug, PartialEq)]
pub struct SHomunculus {
    /// char name[NAME_LENGTH] (at most 23 characters).
    pub name: String,
    pub hom_id: u32,
    pub char_id: u32,
    pub class_: i16,
    pub prev_class: i16,
    pub hp: u32,
    pub max_hp: u32,
    pub sp: u32,
    pub max_sp: u32,
    pub intimacy: u32,
    pub hunger: i16,
    pub hskill: Vec<HSkill>,
    pub scd: Vec<HomScd>,
    pub skillpts: i16,
    pub level: i16,
    pub exp: u64,
    pub rename_flag: i16,
    pub vaporize: i16,
    pub str: i32,
    pub agi: i32,
    pub vit: i32,
    pub int_: i32,
    pub dex: i32,
    pub luk: i32,
    pub str_value: i32,
    pub agi_value: i32,
    pub vit_value: i32,
    pub int_value: i32,
    pub dex_value: i32,
    pub luk_value: i32,
    /// char spiritball (homunculus S).
    pub spiritball: i8,
    pub autofeed: bool,
}

impl Default for SHomunculus {
    /// memset(0).
    fn default() -> Self {
        SHomunculus {
            name: String::new(),
            hom_id: 0,
            char_id: 0,
            class_: 0,
            prev_class: 0,
            hp: 0,
            max_hp: 0,
            sp: 0,
            max_sp: 0,
            intimacy: 0,
            hunger: 0,
            hskill: vec![HSkill::default(); MAX_HOMUNSKILL],
            scd: vec![HomScd::default(); MAX_SKILLCOOLDOWN],
            skillpts: 0,
            level: 0,
            exp: 0,
            rename_flag: 0,
            vaporize: 0,
            str: 0,
            agi: 0,
            vit: 0,
            int_: 0,
            dex: 0,
            luk: 0,
            str_value: 0,
            agi_value: 0,
            vit_value: 0,
            int_value: 0,
            dex_value: 0,
            luk_value: 0,
            spiritball: 0,
            autofeed: false,
        }
    }
}

impl SHomunculus {
    /// The fixed-size arrays of a value that came from elsewhere (a vector of another length is padded / cut).
    pub fn normalized(mut self) -> Self {
        self.hskill.resize(MAX_HOMUNSKILL, HSkill::default());
        self.scd.resize(MAX_SKILLCOOLDOWN, HomScd::default());
        self
    }
}

/// safestrncpy(dst, src, NAME_LENGTH): at most 23 characters.
pub fn name_length(s: &str) -> String {
    s.chars().take(crate::rules::NAME_MAX).collect()
}

/// The homunculus messages of the inter link (both directions).
#[derive(Clone, Debug)]
pub enum HomInter {
    // ---- map → char (int_homun.cpp inter_homunculus_parse_frommap)
    /// 0x3090 intif_homunculus_create.
    Create { account_id: u32, hom: SHomunculus },
    /// 0x3091 intif_homunculus_requestload.
    Load { account_id: u32, homun_id: u32 },
    /// 0x3092 intif_homunculus_requestsave.
    Save { account_id: u32, hom: SHomunculus },
    /// 0x3093 intif_homunculus_requestdelete.
    Delete { homun_id: u32 },
    // ---- char → map (intif.cpp intif_parse)
    /// 0x3890 mapif_homunculus_created (intif_parse_CreateHomunculus).
    Created { account_id: u32, flag: u8, hom: SHomunculus },
    /// 0x3891 mapif_homunculus_loaded (intif_parse_RecvHomunculusData).
    Loaded { account_id: u32, flag: u8, hom: SHomunculus },
    /// 0x3892 mapif_homunculus_saved (intif_parse_SaveHomunculusOk).
    Saved { account_id: u32, flag: bool },
    /// 0x3893 mapif_homunculus_deleted (intif_parse_DeleteHomunculusOk).
    Deleted { flag: bool },
}

// ---------------------------------------------------------------------------------------------- map side (intif.cpp)

/// intif.cpp intif_homunculus_create (0x3090). Always 1 (the char server is part of the module).
pub fn intif_homunculus_create(account_id: u32, sh: &SHomunculus) -> i32 {
    intif::send(Inter::Homun(HomInter::Create { account_id, hom: sh.clone() }));
    1
}

/// intif.cpp intif_homunculus_requestload (0x3091).
pub fn intif_homunculus_requestload(account_id: u32, homun_id: u32) -> i32 {
    intif::send(Inter::Homun(HomInter::Load { account_id, homun_id }));
    1
}

/// intif.cpp intif_homunculus_requestsave (0x3092).
pub fn intif_homunculus_requestsave(account_id: u32, sh: &SHomunculus) -> i32 {
    intif::send(Inter::Homun(HomInter::Save { account_id, hom: sh.clone() }));
    1
}

/// intif.cpp intif_homunculus_requestdelete (0x3093).
pub fn intif_homunculus_requestdelete(homun_id: u32) -> i32 {
    intif::send(Inter::Homun(HomInter::Delete { homun_id }));
    1
}

/// Both sides of the link: the char server's inter_homunculus_parse_frommap and the map server's intif_parse arms.
pub fn dispatch(c: &Ctx, m: HomInter) {
    match m {
        HomInter::Create { account_id, hom } => mapif_parse_homunculus_create(c, account_id, hom),
        HomInter::Load { account_id, homun_id } => mapif_parse_homunculus_load(c, account_id, homun_id),
        HomInter::Save { account_id, hom } => mapif_parse_homunculus_save(c, account_id, hom),
        HomInter::Delete { homun_id } => mapif_parse_homunculus_delete(c, homun_id),
        // intif_parse_CreateHomunculus / intif_parse_RecvHomunculusData: hom_recv_data (the size check cannot fail)
        HomInter::Created { account_id, flag, hom } | HomInter::Loaded { account_id, flag, hom } => {
            crate::homunculus::hom_recv_data(c, account_id, hom, flag as i32);
        }
        HomInter::Saved { account_id, flag } => {
            // intif_parse_SaveHomunculusOk
            if !flag {
                log::error!("homunculus data save failure for account {account_id}");
            }
        }
        HomInter::Deleted { flag } => {
            // intif_parse_DeleteHomunculusOk
            if !flag {
                log::error!("Homunculus data delete failure");
            }
        }
    }
}

// ---------------------------------------------------------------------------------------------- char side (int_homun.cpp)

/// int_homun.cpp mapif_homunculus_save: INSERTs a new homunculus (`hom_id` 0: the new row's AUTO_INCREMENT id is
/// written back into `hd`) or UPDATEs the row `hom_id` (an UPDATE of a missing row changes nothing), then replaces the
/// skills (id > 0 and lv != 0) and the skill cooldowns (skill_id and tick != 0). Always succeeds (the SQL error paths
/// cannot happen here).
pub fn mapif_homunculus_save(c: &Ctx, hd: &mut SHomunculus) -> bool {
    // Sql_EscapeStringLen(…, strnlen(hd->name, NAME_LENGTH))
    let name = name_length(&hd.name);
    let row = |homun_id: u32| HomunculusRow {
        homun_id,
        char_id: hd.char_id,
        class_: hd.class_,
        prev_class: hd.prev_class,
        name: name.clone(),
        level: hd.level,
        exp: hd.exp,
        intimacy: hd.intimacy,
        hunger: hd.hunger,
        str: hd.str,
        agi: hd.agi,
        vit: hd.vit,
        int_: hd.int_,
        dex: hd.dex,
        luk: hd.luk,
        hp: hd.hp,
        max_hp: hd.max_hp,
        sp: hd.sp,
        max_sp: hd.max_sp,
        skill_point: hd.skillpts,
        alive: 1,
        rename_flag: hd.rename_flag,
        vaporize: hd.vaporize,
        autofeed: hd.autofeed,
    };
    if hd.hom_id == 0 {
        // new homunculus: INSERT, then Sql_LastInsertId
        let id = crate::ids::next(c.rc, "homunculus", 1);
        c.db().homunculus().insert(row(id));
        hd.hom_id = id;
    } else if let Some(old) = c.db().homunculus().homun_id().find(hd.hom_id) {
        // `alive` is not part of the UPDATE
        let mut r = row(hd.hom_id);
        r.alive = old.alive;
        c.db().homunculus().homun_id().update(r);
    }
    let homun_id = hd.hom_id;
    // Save skills: DELETE FROM skill_homunculus WHERE homun_id, then one INSERT per learnt skill
    let old: Vec<u64> = c.db().skill_homunculus().by_homun().filter(homun_id).map(|r| r.row_id).collect();
    for id in old {
        c.db().skill_homunculus().row_id().delete(id);
    }
    for s in hd.hskill.iter().take(MAX_HOMUNSKILL) {
        if s.id > 0 && s.lv != 0 {
            c.db().skill_homunculus().insert(SkillHomunculusRow { row_id: 0, homun_id, id: s.id, lv: s.lv as u16 });
        }
    }
    // Save skill cooldowns
    let old: Vec<u64> = c.db().skillcooldown_homunculus().by_homun().filter(homun_id).map(|r| r.row_id).collect();
    for id in old {
        c.db().skillcooldown_homunculus().row_id().delete(id);
    }
    for s in hd.scd.iter().take(MAX_SKILLCOOLDOWN) {
        if s.skill_id == 0 || s.tick == 0 {
            continue;
        }
        // PRIMARY KEY (homun_id, skill): a second entry of the same skill would be a duplicate-key error that ends
        // mapif_homunculus_save with false; hom_save never builds one (hd->scd is a map)
        c.db().skillcooldown_homunculus().insert(SkillcooldownHomunculusRow { row_id: 0, homun_id, skill: s.skill_id, tick: s.tick });
    }
    true
}

/// int_homun.cpp mapif_homunculus_load: the homunculus `homun_id` (intimacy capped to 100000, hunger to 0..100), its
/// skills and its saved cooldowns (at most MAX_SKILLCOOLDOWN). None when there is no such row.
pub fn mapif_homunculus_load(c: &Ctx, homun_id: u32) -> Option<SHomunculus> {
    let r = c.db().homunculus().homun_id().find(homun_id)?;
    let mut hd = SHomunculus {
        hom_id: homun_id,
        char_id: r.char_id,
        class_: r.class_,
        prev_class: r.prev_class,
        name: name_length(&r.name),
        level: r.level,
        exp: r.exp,
        intimacy: r.intimacy,
        hunger: r.hunger,
        str: r.str,
        agi: r.agi,
        vit: r.vit,
        int_: r.int_,
        dex: r.dex,
        luk: r.luk,
        hp: r.hp,
        max_hp: r.max_hp,
        sp: r.sp,
        max_sp: r.max_sp,
        skillpts: r.skill_point,
        rename_flag: r.rename_flag,
        vaporize: r.vaporize,
        autofeed: r.autofeed,
        ..Default::default()
    };
    hd.intimacy = hd.intimacy.min(100000);
    hd.hunger = hd.hunger.clamp(0, 100);
    // Load Homunculus Skill (in PRIMARY KEY order)
    let mut skills: Vec<SkillHomunculusRow> = c.db().skill_homunculus().by_homun().filter(homun_id).collect();
    skills.sort_by_key(|s| s.id);
    for s in skills {
        let i = s.id;
        if i < HM_SKILLBASE || i >= HM_SKILLBASE + MAX_HOMUNSKILL as u16 {
            continue; // invalid skill id
        }
        let idx = (i - HM_SKILLBASE) as usize;
        hd.hskill[idx].id = i;
        hd.hskill[idx].lv = s.lv as u8;
    }
    // Load Homunuclus Skill Cooldown (in PRIMARY KEY order)
    let mut cds: Vec<SkillcooldownHomunculusRow> = c.db().skillcooldown_homunculus().by_homun().filter(homun_id).collect();
    cds.sort_by_key(|s| s.skill);
    let mut count = 0usize;
    for s in cds {
        if count == MAX_SKILLCOOLDOWN {
            log::warn!("Too many skillcooldowns for homunculus {homun_id}, skipping.");
            break;
        }
        if s.skill < HM_SKILLBASE || s.skill >= HM_SKILLBASE + MAX_HOMUNSKILL as u16 {
            continue; // invalid skill ID
        }
        hd.scd[count] = HomScd { skill_id: s.skill, tick: s.tick };
        count += 1;
    }
    // (charserv_config.save_log)
    log::info!("Homunculus loaded (ID: {} - {} / Class: {} / CID: {}).", hd.hom_id, hd.name, hd.class_, hd.char_id);
    Some(hd)
}

/// int_homun.cpp mapif_homunculus_delete: the cooldowns, the skills and the homunculus row go.
pub fn mapif_homunculus_delete(c: &Ctx, homun_id: u32) -> bool {
    delete_rows(c.db(), homun_id);
    log::info!("Homunculus '{homun_id}' has been deleted.");
    true
}

/// The three DELETEs of mapif_homunculus_delete (also char.cpp char_delete's "remove homunculus").
pub fn delete_rows(db: &spacetimedb::Local, homun_id: u32) {
    let ids: Vec<u64> = db.skillcooldown_homunculus().by_homun().filter(homun_id).map(|r| r.row_id).collect();
    for id in ids {
        db.skillcooldown_homunculus().row_id().delete(id);
    }
    let ids: Vec<u64> = db.skill_homunculus().by_homun().filter(homun_id).map(|r| r.row_id).collect();
    for id in ids {
        db.skill_homunculus().row_id().delete(id);
    }
    db.homunculus().homun_id().delete(homun_id);
}

/// int_homun.cpp mapif_parse_homunculus_create (0x3090) → mapif_homunculus_created (0x3890).
pub fn mapif_parse_homunculus_create(c: &Ctx, account_id: u32, mut hom: SHomunculus) {
    let result = mapif_homunculus_save(c, &mut hom);
    intif::send(Inter::Homun(HomInter::Created { account_id, flag: result as u8, hom }));
}

/// int_homun.cpp mapif_parse_homunculus_delete (0x3093) → mapif_homunculus_deleted (0x3893).
pub fn mapif_parse_homunculus_delete(c: &Ctx, homun_id: u32) {
    let result = mapif_homunculus_delete(c, homun_id);
    intif::send(Inter::Homun(HomInter::Deleted { flag: result }));
}

/// int_homun.cpp mapif_parse_homunculus_load (0x3091) → mapif_homunculus_loaded (0x3891: flag 1 with the homunculus,
/// or flag 0 with a zeroed one).
pub fn mapif_parse_homunculus_load(c: &Ctx, account_id: u32, homun_id: u32) {
    let msg = match mapif_homunculus_load(c, homun_id) {
        Some(hd) => HomInter::Loaded { account_id, flag: 1, hom: hd },
        None => HomInter::Loaded { account_id, flag: 0, hom: SHomunculus::default() },
    };
    intif::send(Inter::Homun(msg));
}

/// int_homun.cpp mapif_parse_homunculus_save (0x3092) → mapif_homunculus_saved (0x3892).
pub fn mapif_parse_homunculus_save(c: &Ctx, account_id: u32, mut hom: SHomunculus) {
    let result = mapif_homunculus_save(c, &mut hom);
    intif::send(Inter::Homun(HomInter::Saved { account_id, flag: result }));
}

/// int_homun.cpp mapif_homunculus_rename (0x3094's check): char_name_option 1 allows only the letters of
/// char_name_letters. The map server never sends 0x3094 (it renames through intif_rename, type 2), so this is only
/// kept for completeness of the char server's handler.
pub fn mapif_homunculus_rename(name: &[u8]) -> bool {
    let letters = crate::rules::CHAR_NAME_LETTERS.as_bytes();
    for &b in name.iter().take(crate::mob::NAME_LENGTH) {
        if b == 0 {
            break;
        }
        if !letters.contains(&b) {
            return false;
        }
    }
    true
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn default_arrays() {
        let h = SHomunculus::default();
        assert_eq!(h.hskill.len(), MAX_HOMUNSKILL);
        assert_eq!(h.scd.len(), MAX_SKILLCOOLDOWN);
        let mut h2 = h.clone();
        h2.hskill.truncate(3);
        assert_eq!(h2.normalized().hskill.len(), MAX_HOMUNSKILL);
    }
}
