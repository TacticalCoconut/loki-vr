//! char/int_elemental.cpp: the char server's `elemental` table and its answers to the map server's elemental requests
//! (inter link 0x307c-0x307f → 0x387c-0x387e): creating an elemental (mapif_parse_elemental_create), loading one
//! (mapif_parse_elemental_load), deleting one (mapif_parse_elemental_delete) and saving one
//! (mapif_parse_elemental_save). What travels on the link is [`SElemental`] (mmo.hpp struct s_elemental). The messages
//! go through the inter-link queue (intif.rs), so every answer comes after the map-server function that asked.

use spacetimedb::{table, SpacetimeType, Table};

use crate::companion::MercElemInter;
use crate::intif::{self, Inter};
use crate::world::Ctx;

/// mmo.hpp struct s_elemental: an elemental as the map server keeps it (`ed->elemental`) and as the inter link
/// carries it.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct SElemental {
    pub elemental_id: i32,
    pub char_id: u32,
    pub class_: i16,
    pub mode: i32,
    pub hp: i32,
    pub sp: i32,
    pub max_hp: i32,
    pub max_sp: i32,
    pub matk: i32,
    pub atk: i32,
    pub atk2: i32,
    pub hit: i16,
    pub flee: i16,
    pub amotion: i16,
    pub def: i16,
    pub mdef: i16,
    pub life_time: i64,
}

/// The SQL `elemental` table (sql-files/main.sql). `ele_id` is the table's AUTO_INCREMENT (ids.rs, kind "elemental",
/// from 1).
#[table(accessor = elemental, index(accessor = by_char, btree(columns = [char_id])))]
#[derive(Clone, Debug)]
pub struct ElementalRow {
    #[primary_key]
    pub ele_id: u32,
    pub char_id: u32,
    pub class: i16,
    pub mode: i32,
    pub hp: i32,
    pub sp: i32,
    pub max_hp: i32,
    pub max_sp: i32,
    pub atk1: i32,
    pub atk2: i32,
    pub matk: i32,
    pub aspd: i16,
    pub def: i16,
    pub mdef: i16,
    pub flee: i16,
    pub hit: i16,
    pub life_time: i64,
}

fn row_of(id: u32, e: &SElemental) -> ElementalRow {
    ElementalRow {
        ele_id: id,
        char_id: e.char_id,
        class: e.class_,
        mode: e.mode,
        hp: e.hp,
        sp: e.sp,
        max_hp: e.max_hp,
        max_sp: e.max_sp,
        atk1: e.atk,
        atk2: e.atk2,
        matk: e.matk,
        aspd: e.amotion,
        def: e.def,
        mdef: e.mdef,
        flee: e.flee,
        hit: e.hit,
        life_time: e.life_time,
    }
}

/// int_elemental.cpp mapif_elemental_save: a new elemental (id 0) is INSERTed and gets its id; an existing one is
/// UPDATEd (an UPDATE of a missing row changes nothing).
pub fn mapif_elemental_save(c: &Ctx, ele: &mut SElemental) -> bool {
    if ele.elemental_id == 0 {
        // Create new DB entry
        let id = crate::ids::next(c.rc, "elemental", 1);
        c.db().elemental().insert(row_of(id, ele));
        ele.elemental_id = id as i32;
    } else if c.db().elemental().ele_id().find(ele.elemental_id as u32).is_some() {
        // Update DB entry
        c.db().elemental().ele_id().update(row_of(ele.elemental_id as u32, ele));
    }
    true
}

/// int_elemental.cpp mapif_elemental_load: the row of `ele_id` owned by `char_id`; a missing row: false, with the
/// zeroed s_elemental keeping only the ids.
pub fn mapif_elemental_load(c: &Ctx, ele_id: i32, char_id: u32) -> (bool, SElemental) {
    let mut ele = SElemental { elemental_id: ele_id, char_id, ..Default::default() };
    let Some(r) = c.db().elemental().ele_id().find(ele_id as u32).filter(|r| r.char_id == char_id) else {
        return (false, ele);
    };
    ele.class_ = r.class;
    ele.mode = r.mode;
    ele.hp = r.hp;
    ele.sp = r.sp;
    ele.max_hp = r.max_hp;
    ele.max_sp = r.max_sp;
    ele.atk = r.atk1;
    ele.atk2 = r.atk2;
    ele.matk = r.matk;
    ele.amotion = r.aspd;
    ele.def = r.def;
    ele.mdef = r.mdef;
    ele.flee = r.flee;
    ele.hit = r.hit;
    ele.life_time = r.life_time;
    // (charserv_config.save_log) "Elemental loaded (ID: %d / Class: %d / CID: %d)."
    log::info!("Elemental loaded (ID: {} / Class: {} / CID: {}).", ele.elemental_id, ele.class_, ele.char_id);
    (true, ele)
}

/// int_elemental.cpp mapif_elemental_delete (also char.cpp char_delete's "remove elemental").
pub fn mapif_elemental_delete(db: &spacetimedb::Local, ele_id: i32) -> bool {
    db.elemental().ele_id().delete(ele_id as u32);
    true
}

/// int_elemental.cpp mapif_parse_elemental_create (0x307c): save, then mapif_elemental_send (0x387c).
pub fn mapif_parse_elemental_create(c: &Ctx, mut ele: SElemental) {
    let result = mapif_elemental_save(c, &mut ele);
    intif::send(Inter::MercElem(MercElemInter::ElementalReceived { ele, flag: result }));
}

/// int_elemental.cpp mapif_parse_elemental_load (0x307d): load, then mapif_elemental_send (0x387c).
pub fn mapif_parse_elemental_load(c: &Ctx, ele_id: i32, char_id: u32) {
    let (result, ele) = mapif_elemental_load(c, ele_id, char_id);
    intif::send(Inter::MercElem(MercElemInter::ElementalReceived { ele, flag: result }));
}

/// int_elemental.cpp mapif_parse_elemental_delete (0x307e): delete, then mapif_elemental_deleted (0x387d).
pub fn mapif_parse_elemental_delete(c: &Ctx, ele_id: i32) {
    let result = mapif_elemental_delete(c.db(), ele_id);
    intif::send(Inter::MercElem(MercElemInter::ElementalDeleted { flag: result }));
}

/// int_elemental.cpp mapif_parse_elemental_save (0x307f): save, then mapif_elemental_saved (0x387e).
pub fn mapif_parse_elemental_save(c: &Ctx, mut ele: SElemental) {
    let result = mapif_elemental_save(c, &mut ele);
    intif::send(Inter::MercElem(MercElemInter::ElementalSaved { flag: result }));
}
