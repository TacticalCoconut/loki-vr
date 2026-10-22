//! char/int_pet.cpp: the char server's `pet` table and its answers to the map server's pet requests (inter link
//! 0x3080-0x3083 → 0x3880-0x3883): creating a pet (mapif_create_pet), loading one (mapif_load_pet: pet_info /
//! pet_noinfo), saving one (mapif_save_pet) and deleting one (inter_pet_delete). The SQL row is the `pet` table; what
//! the map server holds of a pet (`pd->pet`) and what travels on the link is [`SPet`] (mmo.hpp struct s_pet). The
//! messages go through the inter-link queue (intif.rs), so every answer comes after the map-server function that
//! asked, as on rAthena.

use spacetimedb::{table, SpacetimeType, Table};

use crate::intif::{self, Inter};
use crate::world::Ctx;

/// The SQL `pet` table (mmo.hpp struct s_pet). `pet_id` is the table's AUTO_INCREMENT: a counter that starts at 1 and
/// never hands out an id twice (ids.rs, kind "pet").
#[table(accessor = pet)]
#[derive(Clone, Debug)]
pub struct Pet {
    #[primary_key]
    pub pet_id: u32,
    pub class_: i16,
    pub name: String,
    pub account_id: u32,
    pub char_id: u32,
    pub level: i16,
    pub egg_id: u32,
    pub equip: u32,
    pub intimate: i16,
    pub hungry: i16,
    pub rename_flag: i8,
    pub incubate: i8,
    pub autofeed: bool,
}

/// mmo.hpp struct s_pet: a pet as the map server keeps it (`pd->pet`) and as the inter link carries it. `pet_id` is
/// int32 in rAthena; ids are positive, 0 = none.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct SPet {
    pub account_id: u32,
    pub char_id: u32,
    pub pet_id: u32,
    pub class_: i16,
    pub level: i16,
    /// pet egg id
    pub egg_id: u32,
    /// pet equip name_id
    pub equip: u32,
    /// pet friendly
    pub intimate: i16,
    /// pet hungry
    pub hungry: i16,
    /// char name[NAME_LENGTH] (at most 23 characters).
    pub name: String,
    pub rename_flag: i8,
    pub incubate: i8,
    pub autofeed: bool,
}

impl SPet {
    fn of(r: &Pet) -> SPet {
        SPet {
            account_id: r.account_id,
            char_id: r.char_id,
            pet_id: r.pet_id,
            class_: r.class_,
            level: r.level,
            egg_id: r.egg_id,
            equip: r.equip,
            intimate: r.intimate,
            hungry: r.hungry,
            name: r.name.clone(),
            rename_flag: r.rename_flag,
            incubate: r.incubate,
            autofeed: r.autofeed,
        }
    }
    fn row(&self, pet_id: u32) -> Pet {
        Pet {
            pet_id,
            class_: self.class_,
            name: self.name.clone(),
            account_id: self.account_id,
            char_id: self.char_id,
            level: self.level,
            egg_id: self.egg_id,
            equip: self.equip,
            intimate: self.intimate,
            hungry: self.hungry,
            rename_flag: self.rename_flag,
            incubate: self.incubate,
            autofeed: self.autofeed,
        }
    }
}

/// safestrncpy(dst, src, NAME_LENGTH): at most 23 characters.
pub fn name_length(s: &str) -> String {
    s.chars().take(crate::rules::NAME_MAX).collect()
}

/// int_pet.cpp inter_pet_tosql: caps hunger (0..100) and intimacy (0..1000), then INSERTs a new pet (`pet_id` None =
/// rAthena's -1: the new row's AUTO_INCREMENT id is written back into `p`) or UPDATEs the row `pet_id` (an UPDATE of a
/// missing row changes nothing). Always succeeds (the SQL error paths cannot happen here).
pub fn inter_pet_tosql(c: &Ctx, pet_id: Option<u32>, p: &mut SPet) -> bool {
    //`pet` (`pet_id`, `class`,`name`,`account_id`,`char_id`,`level`,`egg_id`,`equip`,`intimate`,`hungry`,`rename_flag`,`incubate`,`autofeed`)
    // Sql_EscapeStringLen(…, strnlen(p->name, NAME_LENGTH))
    p.name = name_length(&p.name);
    p.hungry = p.hungry.clamp(0, 100);
    p.intimate = p.intimate.clamp(0, 1000);
    match pet_id {
        None => {
            // New pet: INSERT, then Sql_LastInsertId
            let id = crate::ids::next(c.rc, "pet", 1);
            c.db().pet().insert(p.row(id));
            p.pet_id = id;
        }
        Some(id) => {
            // Update pet.
            if c.db().pet().pet_id().find(id).is_some() {
                c.db().pet().pet_id().update(p.row(id));
            }
        }
    }
    // (charserv_config.save_log: "Pet saved %d - %s.")
    log::info!("Pet saved {} - {}.", pet_id.map(|i| i as i64).unwrap_or(-1), p.name);
    true
}

/// int_pet.cpp inter_pet_fromsql: the row of `pet_id` (hunger / intimacy capped), or a zeroed s_pet (memset) when
/// there is none.
pub fn inter_pet_fromsql(c: &Ctx, pet_id: u32) -> SPet {
    let Some(r) = c.db().pet().pet_id().find(pet_id) else { return SPet::default() };
    let mut p = SPet::of(&r);
    p.pet_id = pet_id;
    p.hungry = p.hungry.clamp(0, 100);
    p.intimate = p.intimate.clamp(0, 1000);
    log::info!("Pet loaded ({pet_id} - {}).", p.name);
    p
}

/// int_pet.cpp mapif_create_pet (0x3080 mapif_parse_CreatePet): the new pet's row, then mapif_pet_created (an egg:
/// `incubate` 1) or mapif_load_pet (a pet that is out).
#[allow(clippy::too_many_arguments)]
pub fn mapif_create_pet(c: &Ctx, account_id: u32, char_id: u32, pet_class: i16, pet_lv: i16, pet_egg_id: u32, pet_equip: u32, intimate: i16, hungry: i16, rename_flag: i8, incubate: i8, pet_name: &str) {
    // memset(pet_pt, 0, …); safestrncpy(pet_pt->name, pet_name, NAME_LENGTH)
    let (acc, chr) = if incubate == 1 { (0, 0) } else { (account_id, char_id) };
    let mut p = SPet {
        account_id: acc,
        char_id: chr,
        pet_id: 0,
        class_: pet_class,
        level: pet_lv,
        egg_id: pet_egg_id,
        equip: pet_equip,
        // mapif_create_pet caps hungry / intimacy (inter_pet_tosql caps them again)
        intimate: intimate.clamp(0, 1000),
        hungry: hungry.clamp(0, 100),
        name: name_length(pet_name),
        rename_flag,
        incubate,
        autofeed: false,
    };
    // pet_id = -1: Signal NEW pet.
    if inter_pet_tosql(c, None, &mut p) {
        if p.incubate != 0 {
            // mapif_pet_created
            log::info!("int_pet: created pet {} - {}", p.pet_id, p.name);
            intif::send(Inter::PetCreated { account_id, pet_class: p.class_, pet_id: p.pet_id });
        } else {
            mapif_load_pet(c, account_id, char_id, p.pet_id);
        }
    } else {
        // Failed...: mapif_pet_created(fd, account_id, nullptr) (class 0, pet id 0)
        intif::send(Inter::PetCreated { account_id, pet_class: 0, pet_id: 0 });
    }
}

/// int_pet.cpp mapif_load_pet (0x3081 mapif_parse_LoadPet): an egg's pet is sent without its owner; a pet that is
/// out only to its owner (mapif_pet_info); anything else is mapif_pet_noinfo (flag 1, a zeroed pet).
pub fn mapif_load_pet(c: &Ctx, account_id: u32, char_id: u32, pet_id: u32) {
    intif::send(mapif_load_pet_answer(c, account_id, char_id, pet_id));
}

/// The answer mapif_load_pet sends (0x3881 mapif_pet_info / mapif_pet_noinfo), for callers that handle it in place.
pub fn mapif_load_pet_answer(c: &Ctx, account_id: u32, char_id: u32, pet_id: u32) -> Inter {
    let mut p = inter_pet_fromsql(c, pet_id);
    if p.incubate == 1 {
        p.account_id = 0;
        p.char_id = 0;
        Inter::PetInfo { account_id, pet: p, flag: 0 }
    } else if account_id == p.account_id && char_id == p.char_id {
        Inter::PetInfo { account_id, pet: p, flag: 0 }
    } else {
        // mapif_pet_noinfo
        Inter::PetInfo { account_id, pet: SPet::default(), flag: 1 }
    }
}

/// int_pet.cpp mapif_save_pet (0x3082 mapif_parse_SavePet): caps hunger / intimacy, saves the row, acks with 0.
pub fn mapif_save_pet(c: &Ctx, account_id: u32, mut data: SPet) {
    data.hungry = data.hungry.clamp(0, 100);
    data.intimate = data.intimate.clamp(0, 1000);
    let id = data.pet_id;
    inter_pet_tosql(c, Some(id), &mut data);
    // mapif_save_pet_ack(fd, account_id, 0)
    intif::send(Inter::SavePetAck { account_id, flag: 0 });
}

/// int_pet.cpp inter_pet_delete (0x3083 mapif_parse_DeletePet → mapif_delete_pet): the row goes; the ack carries
/// inter_pet_delete's return value (always 0).
pub fn inter_pet_delete(c: &Ctx, pet_id: u32) {
    log::info!("delete pet request: {pet_id}...");
    c.db().pet().pet_id().delete(pet_id);
    // mapif_delete_pet_ack(fd, inter_pet_delete(pet_id))
    intif::send(Inter::DeletePetAck { flag: 0 });
}

/// char/inter.cpp mapif_parse_NameChangeRequest (0x3006) for a pet (type 1) or homunculus (type 2): the name is checked
/// against char_name_option / char_name_letters and the answer (mapif_namechange_ack, 0x3806) carries it back.
pub fn mapif_parse_name_change_request(c: &Ctx, account_id: u32, char_id: u32, kind: u8, name: Vec<u8>) {
    let _ = c;
    // Check Authorised letters/symbols in the name (conf/char_athena.conf `char_name_option: 1`: only the letters of
    // char_name_letters are authorised)
    let letters = crate::rules::CHAR_NAME_LETTERS.as_bytes();
    let mut flag = 1u8;
    for &b in name.iter().take(crate::mob::NAME_LENGTH) {
        if b == 0 {
            break;
        }
        if !letters.contains(&b) {
            flag = 0;
            break;
        }
    }
    // (TODO in rAthena: type holds the type of object to rename.) name allowed / refused:
    intif::send(Inter::NameChangeAck { account_id, char_id, kind, flag, name });
}
