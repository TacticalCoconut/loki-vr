//! map/pet.cpp (rAthena e985006, RENEWAL): pets. A pet egg item is never added directly: pet_create_egg asks the char
//! server to create the pet (intif_create_pet → int_pet.rs), and the char server's answer (intif_parse_CreatePet) makes
//! the egg with the new pet's id in its card slots (pet_get_egg). Hatching asks the char server for the pet
//! (intif_request_petdata → pet_recv_petdata → pet_birth_process → pet_data_init); the pet is then a unit of its own
//! (BL_PET) with its pet_data row (`data`), fed, loved, dressed, renamed, evolved, and returned to its egg here.
//! Submodules:
//! - `data`: pet_data (and the capture processes, delayed drops, AI timer) as tables, the pet.hpp enums.
//! - `ai`: pet_ai_hard and pet_ai_sub_hard (following, looting, random walks), pet_lootitem_drop and its delayed drop,
//!   the unit.cpp parts pets need (unit_calc_pos, unit_remove_map / unit_free / unit_warp for BL_PET, the master's
//!   teleport timer).
//! - `bonus`: pet_skill_bonus_timer (pet_skill_bonus), pet_recovery_timer, the heal / support skill timers, the pet
//!   autobonuses (pet_addautobonus / exe / del / end).
//! - `status`: status_calc_pet_, status_calc_misc for pets, status_set_viewdata's BL_PET branch.
//! - `clif`: the pet packets and request handlers.
//! - `buildins`: the pet script commands; `cmd`: the pet @commands.
//! Pet skills (pet_attackskill's unit_skilluse, the support skills' unit_skilluse_id / pos, clif_skill_nodamage of the
//! heal and recovery timers) are the skill families' (next wave): commented stubs at their call sites.

pub mod ai;
pub mod bonus;
pub mod buildins;
pub mod clif;
pub mod cmd;
pub mod data;
pub mod status;

use spacetimedb::Table;

use crate::db::items::CARD0_PET;
use crate::unit::unit as _;
use crate::db::misc::pet::{pet_db, pet_db_search, PetItemType};
use crate::int_pet::SPet;
use crate::intif::{self, Inter};
use crate::inventory::{inv_get, inv_put, pc_additem, pc_delitem, pc_inventoryblank, pc_search_inventory, Item};
use crate::status::defs::SCO_NONE;
use crate::timer::{self, Func};
use crate::unit;
use crate::world::{Ctx, Tick};
use clif::*;
use data::*;

// ---------------------------------------------------------------------------------------------- configuration

/// conf/battle/pet.conf `pet_legacy_formula: no`.
pub fn pet_legacy_formula() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::PET_LEGACY_FORMULA) != 0
}
/// conf/battle/pet.conf `pet_catch_rate: 100`.
pub fn pet_catch_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::PET_CATCH_RATE)
}
/// conf/battle/pet.conf `pet_distance_check: 5`.
pub fn pet_distance_check() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::PET_DISTANCE_CHECK)
}
/// conf/battle/pet.conf `pet_hide_check: yes`.
pub fn pet_hide_check() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::PET_HIDE_CHECK) != 0
}
/// conf/battle/pet.conf `pet_rename: no`.
pub fn pet_rename() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::PET_RENAME) != 0
}
/// conf/battle/pet.conf `pet_friendly_rate: 100`.
pub fn pet_friendly_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::PET_FRIENDLY_RATE)
}
/// conf/battle/pet.conf `pet_hungry_delay_rate: 100`.
pub fn pet_hungry_delay_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::PET_HUNGRY_DELAY_RATE)
}
/// conf/battle/pet.conf `pet_equip_required: yes`.
pub fn pet_equip_required() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::PET_EQUIP_REQUIRED) != 0
}
/// conf/battle/pet.conf `pet_unequip_destroy: yes`.
pub fn pet_unequip_destroy() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::PET_UNEQUIP_DESTROY) != 0
}
/// conf/battle/pet.conf `pet_attack_support: no`, `pet_damage_support: no` (battle/conf.rs has them for battle).
pub fn pet_attack_support() -> bool {
    crate::battle::conf::pet_attack_support()
}
pub fn pet_damage_support() -> bool {
    crate::battle::conf::pet_damage_support()
}
/// conf/battle/pet.conf `pet_support_min_friendly: 900`.
pub fn pet_support_min_friendly() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::PET_SUPPORT_MIN_FRIENDLY)
}
/// conf/battle/pet.conf `pet_status_support: no`.
pub fn pet_status_support() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::PET_STATUS_SUPPORT) != 0
}
/// conf/battle/pet.conf `pet_support_rate: 100`.
pub fn pet_support_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::PET_SUPPORT_RATE)
}
/// conf/battle/pet.conf `pet_lv_rate: 0` (conf.rs).
pub fn pet_lv_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::PET_LV_RATE)
}
/// conf/battle/pet.conf `pet_max_stats: 99`, `pet_max_atk1: 500`, `pet_max_atk2: 1000` (battle.cpp keeps atk1 <= atk2).
pub fn pet_max_stats() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::PET_MAX_STATS)
}
pub fn pet_max_atk1() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::PET_MAX_ATK1)
}
pub fn pet_max_atk2() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::PET_MAX_ATK2)
}
/// conf/battle/pet.conf `pet_disable_in_gvg: no` (battle_config.pet_no_gvg).
pub fn pet_no_gvg() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::PET_DISABLE_IN_GVG) != 0
}
/// conf/battle/pet.conf `pet_ignore_infinite_def: yes`.
pub fn pet_ignore_infinite_def() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::PET_IGNORE_INFINITE_DEF) != 0
}
/// conf/battle/pet.conf `pet_master_dead: no`.
pub fn pet_master_dead() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::PET_MASTER_DEAD) != 0
}
/// conf/battle/pet.conf `pet_autofeed_always: yes`.
pub fn pet_autofeed_always() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::PET_AUTOFEED_ALWAYS) != 0
}
/// conf/battle/client.conf `pet_hair_style: 100`.
pub fn pet_hair_style() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::PET_HAIR_STYLE)
}
/// conf/battle/feature.conf `feature.petevolution: on`, `feature.petautofeed: on`, `feature.pet_autofeed_rate: 89`
/// (all kept: PACKETVER 20211103 >= 20141008).
pub fn feature_petevolution() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::FEATURE_PETEVOLUTION) != 0
}
pub fn feature_pet_autofeed() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::FEATURE_PETAUTOFEED) != 0
}
pub fn feature_pet_autofeed_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FEATURE_PET_AUTOFEED_RATE)
}
/// conf/map_athena.conf `save_settings: 4095` has CHARSAVE_PET (0x008): save after hatching / evolving a pet.
pub const CHARSAVE_PET: bool = true;

// ---------------------------------------------------------------------------------------------- small helpers

/// common/utils.cpp get_percentage: floor(100 * ((double)A / (double)B)) as uint32 (~0 when B is 0, UINT_MAX when too
/// high). The double arithmetic is kept: 29 of 100 is 28 here.
pub fn get_percentage(a: u32, b: u32) -> u32 {
    if b == 0 {
        log::error!("get_percentage: divison by zero! (A={a},B={b})");
        return !0;
    }
    let result = 100.0 * (a as f64 / b as f64);
    if result > u32::MAX as f64 {
        log::error!("get_percentage: result percentage too high! (A={a},B={b},result={result})");
        return u32::MAX;
    }
    result.floor() as u32
}

/// map_id2sd: the player with this account id, in the world.
fn sd_of(c: &Ctx, aid: u32) -> Option<crate::pc::Pc> {
    crate::pc::get(c, aid)
}

/// sd->status.pet_id.
pub fn pet_id_of(c: &Ctx, aid: u32) -> u32 {
    crate::pc::character_of(c, aid).map(|ch| ch.pet_id).unwrap_or(0)
}

/// sd->status.pet_id = v.
pub fn set_pet_id(c: &Ctx, aid: u32, v: u32) {
    if let Some(mut ch) = crate::pc::character_of(c, aid) {
        if ch.pet_id != v {
            ch.pet_id = v;
            crate::status::put_char(c, &ch);
        }
    }
}

/// status_calc_pc(sd, opt).
fn status_calc_pc(c: &Ctx, aid: u32, opt: u8) {
    crate::status::calc_pc(c, aid, opt);
}

/// pet.cpp's pc_delitem `reason` 0 (clif_delitem's ZC_DELETE_ITEM_FROM_BODY delete type).
const DELITEM_NORMAL: i16 = 0;

// ---------------------------------------------------------------------------------------------- intimacy / eggs

/// pet.cpp pet_hungry_val: the hunger level (0 very hungry .. 4 full) of the pet's talk table.
pub fn pet_hungry_val(pd: &PetData) -> i32 {
    let h = pd.pet.hungry as i32;
    if h > PET_HUNGRY_SATISFIED {
        4
    } else if h > PET_HUNGRY_NEUTRAL {
        3
    } else if h > PET_HUNGRY_HUNGRY {
        2
    } else if h > PET_HUNGRY_VERY_HUNGRY {
        1
    } else {
        0
    }
}

/// pet.cpp pet_get_card3_intimacy: the intimacy level kept in an egg's card[3] (bits 1-3).
pub fn pet_get_card3_intimacy(intimacy: i32) -> u32 {
    if intimacy < PET_INTIMATE_SHY {
        1 << 1 // Awkward
    } else if intimacy < PET_INTIMATE_NEUTRAL {
        2 << 1 // Shy
    } else if intimacy < PET_INTIMATE_CORDIAL {
        3 << 1 // Neutral
    } else if intimacy < PET_INTIMATE_LOYAL {
        4 << 1 // Cordial
    } else if intimacy <= PET_INTIMATE_MAX {
        5 << 1 // Loyal
    } else {
        0 // Unknown
    }
}

/// common/utils.hpp MakeDWord(card[1], card[2]): the pet id of an egg.
pub fn egg_pet_id(it: &Item) -> u32 {
    ((it.card2 as u16 as u32) << 16) | it.card1 as u16 as u32
}

/// pet.cpp pet_set_intimate: the new intimacy (capped to 0..1000); the egg of the pet follows (deleted at 0, else its
/// card[3] keeps the rename flag and gets the new level), then the owner's status is recalculated.
pub fn pet_set_intimate(c: &Ctx, pd_id: u32, value: i32) {
    let Some(mut pd) = data::get(c, pd_id) else { return };
    pd.pet.intimate = value.clamp(PET_INTIMATE_NONE, PET_INTIMATE_MAX) as i16;
    put(c, &pd);
    let aid = pd.master;
    let index = pet_egg_search(c, aid, pd.pet.pet_id);
    if pd.pet.intimate as i32 <= PET_INTIMATE_NONE {
        pc_delitem(c, aid, index, 1, 0, DELITEM_NORMAL, crate::map_log::LOG_TYPE_OTHER);
    } else if index >= 0 {
        // Remove everything except the rename flag (rAthena writes through sd->inventory with index -1 when the egg is
        // gone; the module skips that)
        if let Some(mut r) = inv_get(c, aid, index as usize) {
            r.item.card3 &= 1;
            r.item.card3 |= pet_get_card3_intimacy(pd.pet.intimate as i32);
            inv_put(c, &r);
        }
    }
    if sd_of(c, aid).is_some() {
        status_calc_pc(c, aid, SCO_NONE);
    }
}

/// pet.cpp pet_create_egg: true when the pet was requested (the egg arrives with the char server's answer), false when
/// `item_id` is no pet egg, its monster does not exist or the inventory is full (the callers then add the item as a
/// normal item or report the failure).
pub fn pet_create_egg(c: &Ctx, aid: u32, item_id: u32) -> bool {
    let Some(pet) = pet_db_search(item_id as i32, PetItemType::Egg) else { return false }; // No pet egg here.
    let Some(mdb) = crate::db::mobs::mob_db().find(pet.class_ as u32) else { return false };
    if pc_inventoryblank(c, aid) == 0 {
        return false; // Inventory full
    }
    let Some(char_id) = crate::pc::get(c, aid).map(|p| p.char_id) else { return false };
    intif_create_pet(aid, char_id, pet.class_ as i16, mdb.lv as i16, pet.egg_id, 0, pet.intimate as i16, 100, 0, 1, &mdb.jname);
    true
}

/// intif.cpp intif_create_pet (0x3080).
#[allow(clippy::too_many_arguments)]
pub fn intif_create_pet(account_id: u32, char_id: u32, pet_class: i16, pet_lv: i16, egg_id: u32, equip: u32, intimate: i16, hungry: i16, rename_flag: i8, incubate: i8, name: &str) {
    intif::send(Inter::CreatePet { account_id, char_id, pet_class, pet_lv, egg_id, equip, intimate, hungry, rename_flag, incubate, name: crate::int_pet::name_length(name) });
}

/// pet.cpp pet_get_egg (intif_parse_CreatePet, 0x3880): the char server created pet `pet_id`; its egg goes into the
/// owner's inventory. No owner in the world: nothing happens (the pet row stays, as in rAthena).
pub fn pet_get_egg(c: &Ctx, account_id: u32, pet_class: i16, pet_id: u32) -> bool {
    if pet_id == 0 || pet_class == 0 {
        return false;
    }
    // map_id2sd
    if crate::pc::get(c, account_id).is_none() {
        return false;
    }
    // issue: 8150 — the pet's class, not a catch target, says which egg was requested
    let Some(pet) = pet_db().find(pet_class as u16) else {
        intif_delete_petdata(pet_id);
        return false;
    };
    let mut tmp_item = Item::new(pet.egg_id);
    tmp_item.identify = 1;
    tmp_item.card0 = CARD0_PET;
    tmp_item.card1 = pet_id & 0xFFFF; // GetWord(pet_id, 0)
    tmp_item.card2 = (pet_id >> 16) & 0xFFFF; // GetWord(pet_id, 1)
    tmp_item.card3 = 0; // New pets are not named.
    tmp_item.card3 |= pet_get_card3_intimacy(pet.intimate as i32); // Store intimacy status based on initial intimacy
    let ret = pc_additem(c, account_id, &mut tmp_item, 1, crate::map_log::LOG_TYPE_PICKDROP_PLAYER);
    if ret != 0 {
        crate::clif_items::additem(c, account_id, 0, 0, ret);
        intif_delete_petdata(pet_id);
        return false;
    }
    true
}

/// intif.cpp intif_delete_petdata (0x3083): the char server deletes the pet's row.
pub fn intif_delete_petdata(pet_id: u32) {
    intif::send(Inter::DeletePet { pet_id });
}

/// intif.cpp intif_request_petdata (0x3081).
pub fn intif_request_petdata(account_id: u32, char_id: u32, pet_id: u32) {
    intif::send(Inter::LoadPet { account_id, char_id, pet_id });
}

/// intif.cpp intif_save_petdata (0x3082).
pub fn intif_save_petdata(account_id: u32, pet: &SPet) {
    intif::send(Inter::SavePet { account_id, pet: pet.clone() });
}

/// intif.cpp intif_rename_pet → intif_rename(sd, 1, name) (0x3006): safestrncpy(…, name, NAME_LENGTH).
fn intif_rename_pet(c: &Ctx, aid: u32, name: &[u8]) -> i32 {
    let Some(p) = sd_of(c, aid) else { return 0 };
    let mut n: Vec<u8> = name.iter().take_while(|&&b| b != 0).take(crate::rules::NAME_MAX).copied().collect();
    n.resize(crate::mob::NAME_LENGTH, 0);
    intif::send(Inter::NameChange { account_id: aid, char_id: p.char_id, kind: 1, name: n });
    1
}

/// The pet messages of the inter link (intif.rs): the char server's requests (int_pet.cpp, inter.cpp
/// mapif_parse_NameChangeRequest) and the map server's answers (intif.cpp intif_parse_RecvPetData / SavePetOk /
/// DeletePetOk / ChangeNameOk).
pub fn intif_dispatch(c: &Ctx, m: Inter) {
    match m {
        Inter::LoadPet { account_id, char_id, pet_id } => crate::int_pet::mapif_load_pet(c, account_id, char_id, pet_id),
        Inter::SavePet { account_id, pet } => crate::int_pet::mapif_save_pet(c, account_id, pet),
        Inter::NameChange { account_id, char_id, kind, name } => crate::int_pet::mapif_parse_name_change_request(c, account_id, char_id, kind, name),
        Inter::PetInfo { account_id, pet, flag } => {
            // intif_parse_RecvPetData
            pet_recv_petdata(c, account_id, pet, flag);
        }
        Inter::SavePetAck { flag, .. } => {
            // intif_parse_SavePetOk
            if flag == 1 {
                log::error!("pet data save failure");
            }
        }
        Inter::DeletePetAck { flag } => {
            // intif_parse_DeletePetOk
            if flag == 1 {
                log::error!("pet data delete failure");
            }
        }
        Inter::NameChangeAck { account_id, char_id, kind, flag, name } => {
            // intif_parse_ChangeNameOk
            let Some(p) = sd_of(c, account_id) else { return };
            if p.char_id != char_id {
                return;
            }
            match kind {
                0 => {} // Players [NOT SUPPORTED YET]
                1 => {
                    pet_change_name_ack(c, account_id, name, flag as i32);
                }
                2 => crate::homunculus::hom_change_name_ack(c, account_id, name, flag as i32), //Hom
                _ => {}
            }
        }
        _ => {}
    }
}

// ---------------------------------------------------------------------------------------------- targets

/// pet.cpp pet_unlocktarget: the pet drops its target, stops attacking and walking.
pub fn pet_unlocktarget(c: &Ctx, pd_id: u32) {
    let Some(mut pd) = data::get(c, pd_id) else { return };
    pd.target_id = 0;
    put(c, &pd);
    let Some(mut u) = unit::get(c, pd_id) else { return };
    unit::stop_attack(c, &mut u);
    unit::stop_walking(c, &mut u, unit::USW_FIXPOS, 0);
    unit::save(c, &u);
}

/// pet.cpp pet_attackskill: the pet may use its attack skill instead of a normal attack (1 = skill invoked).
/// pet_status_support is off, so it never gets past the first check; the skill use goes through the skill pipeline
/// (skillcore: unit_skilluse_pos / unit_skilluse_id).
pub fn pet_attackskill(c: &Ctx, pd_id: u32, target_id: u32) -> i32 {
    let Some(pd) = data::get(c, pd_id) else { return 0 };
    let Some(a) = pd.a_skill.clone() else { return 0 };
    if !pet_status_support() || (pet_equip_required() && pd.pet.equip == 0) {
        return 0;
    }
    let Some(u) = unit::get(c, pd_id) else { return 0 };
    if u.ud.canact_tick - c.tick() > 0 {
        return 0;
    }
    if c.rnd_chance(a.rate as i32 + pd.pet.intimate as i32 * a.bonusrate as i32 / 1000, 100) {
        // Skotlex: Use pet's skill
        let Some(bl) = unit::get(c, target_id) else { return 0 };
        let range3 = mob_db_of(c, &pd).map(|d| d.range3 as i32).unwrap_or(0);
        if u.map != bl.map || !bl.on_map || crate::status::sc::status_isdead(c, &bl) || !crate::mob::unit::check_distance_bl(&u, &bl, range3) {
            return 0;
        }
        let inf = crate::db::skills::skill_get_inf(a.id);
        if inf & crate::db::skills::INF_GROUND_SKILL as i32 != 0 {
            crate::skill::unit::unit_skilluse_pos(c, pd_id, bl.x, bl.y, a.id, a.lv);
        } else {
            //Offensive self skill? Could be stuff like GX.
            let t = if inf & crate::db::skills::INF_SELF_SKILL as i32 != 0 { pd_id } else { bl.id };
            crate::skill::unit::unit_skilluse_id(c, pd_id, t, a.id, a.lv);
        }
        return 1; //Skill invoked.
    }
    0
}

/// pet.cpp pet_target_check: a monster near the pet may become its target (type 0: the master attacks it, attack_rate;
/// 1: it attacks the master, defence_attack_rate). Always 0.
pub fn pet_target_check(c: &Ctx, pd_id: u32, bl: Option<&unit::Unit>, type_: i32) -> i32 {
    let Some(pd) = data::get(c, pd_id) else { return 0 };
    let Some(bl) = bl else { return 0 };
    let Some(pu) = unit::get(c, pd_id) else { return 0 };
    // status_get_class(bl) of a monster: md->vd->look[LOOK_BASE] (only read once bl is known to be a BL_MOB)
    let bl_class = bl.vd.class;
    if bl.kind != crate::map::BL_MOB || !bl.on_map || (pd.pet.intimate as i32) < pet_support_min_friendly() || pd.pet.hungry as i32 <= PET_HUNGRY_NONE || pd.pet.class_ as i32 == bl_class {
        return 0;
    }
    let range2 = mob_db_of(c, &pd).map(|d| d.range2 as i32).unwrap_or(0);
    if pu.map != bl.map || !crate::mob::unit::check_distance_bl(&pu, bl, range2) {
        return 0;
    }
    if !pet_master_dead() && crate::pc::isdead(c, pd.master) {
        pet_unlocktarget(c, pd_id);
        return 0;
    }
    if !crate::mob::unit::status_check_skilluse(c, Some(&pu), Some(bl), 0, 0) {
        return 0;
    }
    let Some(pdb) = pet_db_of(&pd) else { return 0 };
    let mut rate: i32;
    if type_ == 0 {
        rate = pdb.attack_rate as i32;
        rate = rate * pd.rate_fix as i32 / 1000;
        if pdb.attack_rate > 0 && rate <= 0 {
            rate = 1;
        }
    } else {
        rate = pdb.defence_attack_rate as i32;
        rate = rate * pd.rate_fix as i32 / 1000;
        if pdb.defence_attack_rate > 0 && rate <= 0 {
            rate = 1;
        }
    }
    if c.rnd_chance(rate, 10000) {
        let mut pd = pd;
        if pd.target_id == 0 || c.rnd_chance(pdb.change_target_rate as i32, 10000) {
            pd.target_id = bl.id;
            put(c, &pd);
        }
    }
    0
}

/// pet.cpp pet_sc_check: a status change of `type_` just started on the owner; a pet whose recovery cures it arms its
/// recovery timer. 1 = nothing to do.
pub fn pet_sc_check(c: &Ctx, aid: u32, type_: i32) -> i32 {
    let Some(mut pd) = of_master(c, aid) else { return 1 };
    let Some(mut rec) = pd.recovery.clone() else { return 1 };
    if (pet_equip_required() && pd.pet.equip == 0) || rec.timer != 0 || rec.sc_type != type_ {
        return 1;
    }
    rec.timer = timer::add(c, c.tick() + rec.delay as Tick * 1000, Func::PetRecoveryTimer, aid, 0);
    pd.recovery = Some(rec);
    put(c, &pd);
    0
}

// ---------------------------------------------------------------------------------------------- hunger

/// The hunger interval of a pet (pet_db HungryDelay × pet_hungry_delay_rate).
fn hungry_interval(pdb: &crate::db::misc::pet::PetDb) -> i32 {
    if pet_hungry_delay_rate() != 100 {
        (pdb.hungry_delay as i64 * pet_hungry_delay_rate() as i64 / 100) as i32
    } else {
        pdb.hungry_delay as i32
    }
}

/// pet.cpp pet_hungry (timer, target = the owner's account id): the pet gets hungrier; a starving pet loses intimacy
/// every 20 s. With autofeed on and hunger at feature.pet_autofeed_rate or less, the pet is fed.
pub fn pet_hungry(c: &Ctx, tid: u64, tick: Tick, id: u32) -> i32 {
    if sd_of(c, id).is_none() {
        return 1;
    }
    if pet_id_of(c, id) == 0 {
        return 1;
    }
    let Some(mut pd) = of_master(c, id) else { return 1 };
    if pd.pet_hungry_timer != tid {
        log::error!("pet_hungry_timer {} != {}", pd.pet_hungry_timer, tid);
        return 0;
    }
    pd.pet_hungry_timer = 0;
    put(c, &pd);
    if pd.pet.intimate as i32 <= PET_INTIMATE_NONE {
        return 1; //You lost the pet already, the rest is irrelevant.
    }
    let Some(pdb) = pet_db_of(&pd) else { return 1 };
    let mut interval = hungry_interval(pdb);
    pd.pet.hungry = (pd.pet.hungry as i32 - pdb.fullness as i32) as i16;
    put(c, &pd);
    if (pd.pet.hungry as i32) < PET_HUNGRY_NONE {
        if let Some(mut u) = unit::get(c, pd.id) {
            unit::stop_attack(c, &mut u);
            unit::save(c, &u);
        }
        pd.pet.hungry = PET_HUNGRY_NONE as i16;
        put(c, &pd);
        pet_set_intimate(c, pd.id, pd.pet.intimate as i32 + pdb.hungry_intimacy_dec);
        let Some(mut p2) = data::get(c, pd.id) else { return 0 };
        if p2.pet.intimate as i32 <= PET_INTIMATE_NONE {
            p2.status.speed = get_pet_walk_speed(c, &p2);
            put(c, &p2);
        }
        status::status_calc_pet(c, pd.id, SCO_NONE);
        let Some(p3) = data::get(c, pd.id) else { return 0 };
        clif_send_petdata(c, Some(id), &p3, CHANGESTATEPET_INTIMACY);
        interval = 20000; // While starving, it's every 20 seconds
    }
    let Some(pd) = data::get(c, pd.id) else { return 0 };
    clif_send_petdata(c, Some(id), &pd, CHANGESTATEPET_HUNGER);
    if feature_pet_autofeed() && pd.pet.autofeed && pd.pet.hungry as i32 <= feature_pet_autofeed_rate() {
        pet_food(c, id, pd.id);
    }
    let interval = interval.max(1);
    if let Some(mut pd) = data::get(c, pd.id) {
        pd.pet_hungry_timer = timer::add(c, tick + interval as Tick, Func::PetHungry, id, 0);
        put(c, &pd);
    }
    0
}

/// pet.cpp pet_hungry_timer_delete.
pub fn pet_hungry_timer_delete(c: &Ctx, pd_id: u32) -> i32 {
    let Some(mut pd) = data::get(c, pd_id) else { return 0 };
    if pd.pet_hungry_timer != 0 {
        timer::delete(c, pd.pet_hungry_timer);
        pd.pet_hungry_timer = 0;
        put(c, &pd);
    }
    1
}

// ---------------------------------------------------------------------------------------------- menu actions

/// pet.cpp pet_performance: the pet stops (2 s) and performs (normal 1-3 / special 4 by intimacy), then drops its loot.
fn pet_performance(c: &Ctx, _aid: u32, pd_id: u32) -> i32 {
    if let Some(mut u) = unit::get(c, pd_id) {
        unit::stop_walking(c, &mut u, unit::USW_NONE, 2000);
        unit::save(c, &u);
    }
    let Some(pd) = data::get(c, pd_id) else { return 1 };
    clif_send_petdata(c, None, &pd, CHANGESTATEPET_PERFORMANCE);
    ai::pet_lootitem_drop(c, pd_id, None);
    1
}

/// pet.cpp pet_return_egg: the pet goes back into its egg (the egg is shown again, the pet is saved as an egg and
/// freed). False when the egg is not in the inventory.
pub fn pet_return_egg(c: &Ctx, aid: u32, pd_id: u32) -> bool {
    ai::pet_lootitem_drop(c, pd_id, Some(aid));
    let Some(mut pd) = data::get(c, pd_id) else { return false };
    let i = pet_egg_search(c, aid, pd.pet.pet_id);
    if i == -1 {
        return false;
    }
    if let Some(mut r) = inv_get(c, aid, i as usize) {
        r.item.attribute = 0;
        inv_put(c, &r); // sd->inventory.dirty = true
    }
    pd.pet.incubate = 1;
    put(c, &pd);
    // PACKETVER >= 20180704
    crate::clif_items::inventorylist(c, aid);
    clif_send_petdata(c, Some(aid), &pd, CHANGESTATEPET_UPDATE_EGG);
    ai::unit_free(c, pd_id, unit::CLR_OUTSIGHT);
    status_calc_pc(c, aid, SCO_NONE);
    set_pet_id(c, aid, 0);
    true
}

/// pet.cpp pet_data_init: the pet comes out (its pet_data and unit next to the owner, its status, the bonus script's
/// recalculation and its hunger timer). False when the pet is not the owner's.
pub fn pet_data_init(c: &Ctx, aid: u32, pet: &SPet) -> bool {
    let Some(p) = sd_of(c, aid) else { return false };
    let Some(sdu) = unit::get(c, aid) else { return false };
    if aid != pet.account_id || p.char_id != pet.char_id {
        set_pet_id(c, aid, 0);
        return false;
    }
    let cur = pet_id_of(c, aid);
    if cur != pet.pet_id {
        if cur != 0 {
            //Wrong pet?? Set incubate to no and send it back for saving.
            let mut pet = pet.clone();
            pet.incubate = 1;
            intif_save_petdata(aid, &pet);
            set_pet_id(c, aid, 0);
            return false;
        }
        //The pet_id value was lost? odd... restore it.
        set_pet_id(c, aid, pet.pet_id);
    }
    let Some(pet_db_ptr) = pet_db().find(pet.class_ as u16) else {
        set_pet_id(c, aid, 0);
        return false;
    };
    if let Some(old) = of_master(c, aid) {
        // rAthena would leak the old pet_data (sd->pd is overwritten); one pet per owner here
        log::error!("pet_data_init: player {aid} already has pet {}; it is freed", old.id);
        ai::unit_free(c, old.id, unit::CLR_OUTSIGHT);
    }
    let id = crate::mob::spawn::npc_get_new_npc_id(c);
    let tick = c.tick();
    let pd = PetData {
        id,
        master: aid,
        pet: pet.clone(),
        db_mob_id: pet.class_ as u16 as u32,
        status: Default::default(),
        pet_hungry_timer: 0,
        target_id: 0,
        skillbonus: false,
        move_fail_count: 0,
        next_walktime: 0,
        last_thinktime: 0,
        rate_fix: 0,
        recovery: None,
        bonus: None,
        a_skill: None,
        s_skill: None,
        loot: None,
        autobonus: Vec::new(),
        autobonus2: Vec::new(),
        autobonus3: Vec::new(),
        masterteleport_timer: 0,
    };
    // pd->type = BL_PET; pd->id = npc_get_new_npc_id(); unit_dataset(pd); pd->ud.dir = sd->ud.dir
    let mut u = unit::Unit {
        id,
        kind: crate::map::BL_PET,
        map: sdu.map,
        x: sdu.x,
        y: sdu.y,
        on_map: false,
        name: pet.name.clone(),
        vd: Default::default(),
        // unit_dataset: every timer INVALID_TIMER, attackabletime / canact_tick / canmove_tick / endure_tick = now,
        // sx / sy 8; then pd->ud.dir = sd->ud.dir
        ud: unit::UnitData { dir: sdu.ud.dir, attackabletime: tick, canact_tick: tick, canmove_tick: tick, endure_tick: tick, dmg_tick: 0, sx: 8, sy: 8, ..Default::default() },
        sc: Default::default(),
        speed: 0,
        mode: 0,
        level: pet.level as i32,
        char_id: 0,
        head_dir: 0,
        font: 0,
        manner: 0,
        karma: false,
        guild_id: 0,
        emblem_id: 0,
        hp: 0,
        max_hp: 0,
        dead: false,
        size: 0,
        boss: 0,
        invisible: false,
        npc_owner_char: 0,
    };
    // map_addiddb(pd): the unit exists (off the map) from now on
    c.db().unit().insert(u.clone());
    put(c, &pd);
    status::status_set_viewdata(c, id, pet.class_ as i32);
    // unit_calc_pos(pd, sd->x, sd->y, sd->ud.dir) from the owner's cell
    ai::unit_calc_pos(c, id, sdu.x as i32, sdu.y as i32, sdu.ud.dir);
    if let Some(mut u2) = unit::get(c, id) {
        u2.x = u2.ud.to_x;
        u2.y = u2.ud.to_y;
        unit::save(c, &u2);
        u = u2;
    }
    let _ = u;
    status::status_calc_pet(c, id, crate::status::defs::SCO_FIRST);
    let Some(mut pd) = data::get(c, id) else { return false };
    pd.last_thinktime = tick;
    pd.skillbonus = false;
    put(c, &pd);
    if pet_status_support() {
        // run_script(pet_db_ptr->pet_support_script, 0, sd->id, 0)
        bonus::run_support_script(c, aid, pet_db_ptr);
    }
    if pet_db_ptr.pet_bonus_script.is_some() {
        status_calc_pc(c, aid, SCO_NONE);
    }
    let Some(mut pd) = data::get(c, id) else { return false };
    let mut interval = hungry_interval(pet_db_ptr);
    if pd.pet.hungry as i32 <= PET_HUNGRY_NONE {
        interval = 20000; // While starving, it's every 20 seconds
    }
    let interval = interval.max(1);
    pd.pet_hungry_timer = timer::add(c, c.tick() + interval as Tick, Func::PetHungry, aid, 0);
    pd.masterteleport_timer = 0;
    if pd.pet.rename_flag == 0 {
        if let Some(mdb) = mob_db_of(c, &pd) {
            pd.pet.name = crate::int_pet::name_length(&mdb.jname);
        }
    }
    put(c, &pd);
    // do_init_pet's pet_ai_hard interval timer (the module arms it with the first pet)
    ai::ensure_ai_timer(c);
    true
}

/// pet.cpp pet_birth_process: the pet hatches (saved as out of its egg, the inventory saved, and shown when the owner
/// is on a map). 1 = failure.
pub fn pet_birth_process(c: &Ctx, aid: u32, pet: &SPet) -> i32 {
    if sd_of(c, aid).is_none() {
        return 1;
    }
    if pet_id_of(c, aid) != 0 && pet.incubate == 1 {
        set_pet_id(c, aid, 0);
        return 1;
    }
    let Some(p) = sd_of(c, aid) else { return 1 };
    let mut pet = pet.clone();
    pet.incubate = 0;
    pet.account_id = aid;
    pet.char_id = p.char_id;
    set_pet_id(c, aid, pet.pet_id);
    if !pet_data_init(c, aid, &pet) {
        return 1;
    }
    intif_save_petdata(aid, &pet);
    if CHARSAVE_PET {
        chrif_save_inventory(c, aid); //is it REALLY Needed to save the char for hatching a pet? [Skotlex]
    }
    let on_map = unit::get(c, aid).map(|u| u.on_map).unwrap_or(false);
    if on_map {
        let Some(pd) = of_master(c, aid) else { return 1 };
        let Some(mut pu) = unit::get(c, pd.id) else { return 1 };
        if !unit::addblock(c, &mut pu) {
            return 1;
        }
        crate::clif::spawn(c, &pu, false);
        let pd = data::get(c, pd.id).unwrap_or(pd);
        clif_send_petdata(c, Some(aid), &pd, CHANGESTATEPET_INIT);
        clif_send_petdata(c, Some(aid), &pd, CHANGESTATEPET_HAIRSTYLE);
        // PACKETVER >= 20180704
        clif_send_petdata(c, Some(aid), &pd, CHANGESTATEPET_UPDATE_EGG);
        clif_send_petdata(c, None, &pd, CHANGESTATEPET_ACCESSORY);
        clif_send_petstatus(c, aid, &pd);
        clif_pet_autofeed_status(c, aid, true);
    }
    0
}

/// chrif_save(sd, CSAVE_INVENTORY) as pet_birth_process / pet_evolution call it (CHARSAVE_PET; chrif.rs).
pub fn chrif_save_inventory(c: &Ctx, aid: u32) {
    crate::chrif::chrif_save(c, aid, crate::chrif::CSAVE_INVENTORY);
}

/// chrif.cpp chrif_save's pet part: `if( sd->status.pet_id > 0 && sd->pd ) intif_save_petdata(…, &sd->pd->pet)`.
pub fn chrif_save_pet(c: &Ctx, aid: u32) {
    if pet_id_of(c, aid) > 0 {
        if let Some(pd) = of_master(c, aid) {
            intif_save_petdata(aid, &pd.pet);
        }
    }
}

/// pet.cpp pet_recv_petdata (intif_parse_RecvPetData): the char server's answer to intif_request_petdata. An egg's
/// pet hatches (its egg is marked broken, attribute 1, and pet_birth_process runs); a pet that was out (login) is
/// initialised and shown when the owner is on a map. flag 1: no such pet for this owner.
pub fn pet_recv_petdata(c: &Ctx, account_id: u32, p: SPet, flag: u8) -> i32 {
    if sd_of(c, account_id).is_none() {
        return 1;
    }
    if flag == 1 {
        set_pet_id(c, account_id, 0);
        return 1;
    }
    if p.incubate == 1 {
        let i = pet_egg_search(c, account_id, p.pet_id);
        if i == -1 {
            log::error!("pet_recv_petdata: Hatching pet ({}:{}) aborted, couldn't find egg in inventory!", p.pet_id, p.name);
            set_pet_id(c, account_id, 0);
            return 1;
        }
        // Hide egg from inventory. Set pet egg to broken, before the inventory gets saved
        if let Some(mut r) = inv_get(c, account_id, i as usize) {
            r.item.attribute = 1;
            inv_put(c, &r);
        }
        // Hatch the pet
        pet_birth_process(c, account_id, &p);
    } else {
        pet_data_init(c, account_id, &p);
        let on_map = unit::get(c, account_id).map(|u| u.on_map).unwrap_or(false);
        if let Some(pd) = of_master(c, account_id) {
            if on_map {
                let Some(mut pu) = unit::get(c, pd.id) else { return 1 };
                if !unit::addblock(c, &mut pu) {
                    return 1;
                }
                crate::clif::spawn(c, &pu, false);
                let pd = data::get(c, pd.id).unwrap_or(pd);
                clif_send_petdata(c, Some(account_id), &pd, CHANGESTATEPET_INIT);
                clif_send_petdata(c, Some(account_id), &pd, CHANGESTATEPET_HAIRSTYLE);
                clif_send_petdata(c, None, &pd, CHANGESTATEPET_ACCESSORY);
                clif_send_petstatus(c, account_id, &pd);
            }
        }
    }
    0
}

/// pet.cpp pet_select_egg: the egg chosen in the incubator's list asks the char server for its pet.
pub fn pet_select_egg(c: &Ctx, aid: u32, egg_index: i16) -> i32 {
    let Some(p) = sd_of(c, aid) else { return 0 };
    if egg_index < 0 || egg_index as usize >= crate::inventory::MAX_INVENTORY {
        return 0; //Forged packet!
    }
    if p.trading {
        return 0; //The player have trade in progress.
    }
    let it = inv_get(c, aid, egg_index as usize).map(|r| r.item).unwrap_or_default();
    if pet_db_search(it.nameid as i32, PetItemType::Egg).is_none() {
        log::error!("pet does not exist, egg id {}", it.nameid);
        return 0;
    }
    if it.card0 == CARD0_PET {
        intif_request_petdata(aid, p.char_id, egg_pet_id(&it));
    } else {
        log::error!("wrong egg item inventory {egg_index}");
    }
    0
}

// ---------------------------------------------------------------------------------------------- capture

/// pet.cpp pet_catch_process_start: the taming item was used; the player gets the taming cursor (ZC_START_CAPTURE)
/// and the capture waits for the monster he picks.
pub fn pet_catch_process_start(c: &Ctx, aid: u32, item_id: u32, flag: u8) {
    let Some(p) = sd_of(c, aid) else { return };
    let Some(sdu) = unit::get(c, aid) else { return };
    if crate::npc::mapflag(c, sdu.map, crate::inventory::MF_NOPETCAPTURE) {
        crate::clif::displaymessage(c, &sdu, crate::data::msg_txt(669)); // You can't catch any pet on this map.
        return;
    }
    // Reuse previously allocated memory and restart the process
    let row = PetCatchProcess { char_id: p.char_id, taming_item: item_id, flag };
    if c.db().pet_catch_process().char_id().find(p.char_id).is_some() {
        c.db().pet_catch_process().char_id().update(row);
    } else {
        c.db().pet_catch_process().insert(row);
    }
    clif_catch_process(c, aid);
}

/// pet_catchprocesses.erase(char_id).
fn catch_erase(c: &Ctx, char_id: u32) {
    c.db().pet_catch_process().char_id().delete(char_id);
}

/// pet.cpp pet_catch_process_end: the player picked monster `target_id` (CZ_TRYCAPTURE_MONSTER). The capture fails
/// (roulette 0) on a wrong item, a boss for universal lures, distance, a full inventory or a hidden monster; else the
/// rate decides: on success the monster is taken (AG_TAMING, removed and killed) and the char server makes the egg.
pub fn pet_catch_process_end(c: &Ctx, aid: u32, target_id: u32) {
    use crate::status::consts::{SC_CAMOUFLAGE, SC_CLOAKING, SC_CLOAKINGEXCEED, SC_HIDING, SC_NEWMOON};
    let Some(p) = sd_of(c, aid) else { return };
    let Some(sdu) = unit::get(c, aid) else { return };
    let char_id = p.char_id;
    let Some(process) = c.db().pet_catch_process().char_id().find(char_id) else {
        clif_pet_roulette(c, aid, false);
        return;
    };
    // map_id2md
    let md = crate::mob::get(c, target_id);
    let mdu = unit::get(c, target_id).filter(|u| u.kind == crate::map::BL_MOB);
    let (Some(md), Some(mdu)) = (md, mdu.filter(|u| u.on_map)) else {
        // Invalid inputs/state, abort capture.
        clif_pet_roulette(c, aid, false);
        catch_erase(c, char_id);
        return;
    };
    if crate::npc::mapflag(c, sdu.map, crate::inventory::MF_NOPETCAPTURE) {
        clif_pet_roulette(c, aid, false);
        catch_erase(c, char_id);
        crate::clif::displaymessage(c, &sdu, crate::data::msg_txt(669)); // You can't catch any pet on this map.
        return;
    }
    //FIXME: delete taming item here, if this was an item-invoked capture and the item was flagged as delay-consume [ultramage]
    let Some(pet) = pet_db().find(md.mob_id as u16) else {
        clif_pet_roulette(c, aid, false);
        catch_erase(c, char_id);
        return;
    };
    match process.flag {
        PET_CATCH_NORMAL => {
            // If the taming item used is different from the taming item according to the pet database
            if process.taming_item != pet.item_id {
                clif_pet_roulette(c, aid, false);
                catch_erase(c, char_id);
                return;
            }
        }
        PET_CATCH_UNIVERSAL_NO_BOSS => {
            // PET_CATCH_UNIVERSAL_NO_BOSS is used for universal lures (except bosses for now).
            if crate::mob::has_mode(&md, crate::db::mobs::MD_STATUSIMMUNE) {
                clif_pet_roulette(c, aid, false);
                catch_erase(c, char_id);
                return;
            }
        }
        // PET_CATCH_UNIVERSAL_ALL: No checks, catch anything.
        _ => {}
    }
    if pet_distance_check() != 0 && crate::mob::unit::distance_bl(&sdu, &mdu) > pet_distance_check() {
        clif_pet_roulette(c, aid, false);
        catch_erase(c, char_id);
        return;
    }
    if pc_inventoryblank(c, aid) == 0 {
        clif_pet_roulette(c, aid, false);
        catch_erase(c, char_id);
        crate::clif_items::msg_color(c, &sdu, crate::clif_items::MSI_CANT_GET_ITEM_BECAUSE_COUNT, crate::social::COLOR_RED);
        return;
    }
    let has = |t| crate::status::sc::has_sce(c, target_id, t);
    if pet_hide_check() && (has(SC_HIDING) || has(SC_CLOAKING) || has(SC_CAMOUFLAGE) || has(SC_NEWMOON) || has(SC_CLOAKINGEXCEED)) {
        clif_pet_roulette(c, aid, false);
        catch_erase(c, char_id);
        return;
    }
    let base_level = crate::pc::character_of(c, aid).map(|ch| ch.base_level as i32).unwrap_or(1);
    let luk = crate::status::data::sd_get(c, aid).map(|sd| sd.battle_status.luk as i32).unwrap_or(0);
    let capture = pet.capture as u32;
    let percent = get_percentage(md.status.hp, md.status.max_hp);
    let mut pet_catch_rate: i32 = if pet_legacy_formula() {
        // int * uint32 (get_percentage): the product and the division are uint32
        let a = (pet.capture as i32 + (base_level - md.level) * 30 + luk * 20) as u32;
        (a.wrapping_mul(200u32.wrapping_sub(percent)) / 100) as i32
    } else {
        // uint32 arithmetic: get_percentage returns uint32
        capture.wrapping_add(100u32.wrapping_sub(percent).wrapping_mul(capture) / 100) as i32
    };
    if pet_catch_rate < 1 {
        pet_catch_rate = 1;
    }
    if crate::pet::pet_catch_rate() != 100 {
        pet_catch_rate = pet_catch_rate * crate::pet::pet_catch_rate() / 100;
    }
    if c.rnd_chance(pet_catch_rate, 10000) {
        crate::achievement::achievement_update_objective(c, Some(aid), crate::db::misc::achievement::AG_TAMING, &[md.mob_id]);
        unit::remove_map(c, target_id, unit::CLR_OUTSIGHT);
        crate::status::hp::status_kill(c, target_id);
        clif_pet_roulette(c, aid, true);
        if let Some(mdb) = crate::db::mobs::mob_db().find(pet.class_ as u32) {
            intif_create_pet(aid, char_id, pet.class_ as i16, mdb.lv as i16, pet.egg_id, 0, pet.intimate as i16, 100, 0, 1, &mdb.jname);
        }
    } else {
        clif_pet_roulette(c, aid, false);
    }
    catch_erase(c, char_id);
}

// ---------------------------------------------------------------------------------------------- menu, name, equipment

/// pet.cpp pet_menu (CZ_COMMAND_PET): 0 status, 1 feed, 2 performance, 3 return to egg, 4 unequip. 1 = no pet.
pub fn pet_menu(c: &Ctx, aid: u32, menunum: i32) -> i32 {
    if sd_of(c, aid).is_none() {
        return 0;
    }
    let Some(pd) = of_master(c, aid) else { return 1 };
    //You lost the pet already.
    if pet_id_of(c, aid) == 0 || pd.pet.intimate as i32 <= PET_INTIMATE_NONE || pd.pet.incubate != 0 {
        return 1;
    }
    match menunum {
        0 => clif_send_petstatus(c, aid, &pd),
        1 => {
            pet_food(c, aid, pd.id);
        }
        2 => {
            pet_performance(c, aid, pd.id);
        }
        3 => {
            pet_return_egg(c, aid, pd.id);
        }
        4 => {
            pet_unequipitem(c, aid, pd.id);
        }
        _ => {}
    }
    0
}

/// pet.cpp pet_change_name (CZ_RENAME_PET): control characters refuse the name at once; else the char server checks it
/// (intif_rename_pet). A renamed pet cannot be renamed again (pet_rename: no).
pub fn pet_change_name(c: &Ctx, aid: u32, name: &[u8]) -> i32 {
    if sd_of(c, aid).is_none() {
        return 1;
    }
    let Some(pd) = of_master(c, aid) else { return 1 };
    if pd.pet.rename_flag == 1 && !pet_rename() {
        return 1;
    }
    for &b in name.iter().take(crate::mob::NAME_LENGTH) {
        if b == 0 {
            break;
        }
        // `char` is signed: bytes 0x80.. have bit 0xe0 set
        if b & 0xe0 == 0 || b == 0x7f {
            return 1;
        }
    }
    intif_rename_pet(c, aid, name)
}

/// strlib.cpp normalize_name(str, " "): leading and trailing blanks go, inner runs become one space.
pub(crate) fn normalize_name(s: &[u8]) -> Vec<u8> {
    let s: Vec<u8> = s.iter().take_while(|&&b| b != 0).copied().collect();
    let mut out = Vec::with_capacity(s.len());
    let mut i = 0;
    // trim start of string
    while i < s.len() && s[i] == b' ' {
        i += 1;
    }
    let mut put_space = false;
    while i < s.len() {
        if put_space {
            // replace trim characters with a single space
            out.push(b' ');
        }
        // copy non trim characters
        while i < s.len() && s[i] != b' ' {
            out.push(s[i]);
            i += 1;
        }
        // skip trim characters
        while i < s.len() && s[i] == b' ' {
            i += 1;
        }
        put_space = true;
    }
    out
}

/// pet.cpp pet_change_name_ack (intif_parse_ChangeNameOk): the char server's verdict on a new name. Refused or empty:
/// message 280 and the status again; accepted: the name, the rename flag (also in the egg's card[3]).
pub fn pet_change_name_ack(c: &Ctx, aid: u32, name: Vec<u8>, flag: i32) -> i32 {
    let Some(mut pd) = of_master(c, aid) else { return 0 };
    let Some(sdu) = unit::get(c, aid) else { return 0 };
    let name = normalize_name(&name); //bugreport:3032
    if flag == 0 || name.is_empty() {
        crate::clif::displaymessage(c, &sdu, crate::data::msg_txt(280)); // You cannot use this name for your pet.
        clif_send_petstatus(c, aid, &pd); //Send status so client knows pet name change got rejected.
        return 0;
    }
    // safestrncpy(pd->pet.name, name, NAME_LENGTH)
    let n: Vec<u8> = name.into_iter().take(crate::rules::NAME_MAX).collect();
    pd.pet.name = crate::net::wire::from_latin1(&n);
    put(c, &pd);
    if let Some(pu) = unit::get(c, pd.id) {
        crate::clif::name_area(c, &pu);
    }
    pd.pet.rename_flag = 1;
    put(c, &pd);
    clif_send_petdata(c, None, &pd, CHANGESTATEPET_ACCESSORY);
    clif_send_petstatus(c, aid, &pd);
    let index = pet_egg_search(c, aid, pd.pet.pet_id);
    if index >= 0 {
        if let Some(mut r) = inv_get(c, aid, index as usize) {
            r.item.card3 |= 1;
            inv_put(c, &r);
        }
    }
    1
}

/// pet.cpp pet_equipitem (clif_parse_EquipItem with a pet accessory): the pet's accessory goes on (the item leaves the
/// inventory); a wrong or second accessory gets the equip-fail ack. With pet_equip_required the support timers start.
pub fn pet_equipitem(c: &Ctx, aid: u32, index: i32) -> i32 {
    let Some(mut pd) = of_master(c, aid) else { return 1 };
    let Some(pdb) = pet_db_of(&pd) else { return 1 };
    let nameid = inv_get(c, aid, index.max(0) as usize).map(|r| r.item.nameid).unwrap_or(0);
    if pdb.acce_id == 0 || nameid != pdb.acce_id || pd.pet.equip != 0 {
        crate::clif_items::equipitemack(c, aid, crate::inventory::ITEM_EQUIP_ACK_FAIL, index, 0);
        return 1;
    }
    pc_delitem(c, aid, index, 1, 0, DELITEM_NORMAL, crate::map_log::LOG_TYPE_OTHER);
    pd.pet.equip = nameid;
    put(c, &pd);
    status::status_set_viewdata(c, pd.id, pd.pet.class_ as i32); //Updates view_data.
    clif_send_petdata(c, None, &pd, CHANGESTATEPET_ACCESSORY);
    if pet_equip_required() {
        // Skotlex: start support timers if need
        let tick = c.tick();
        let Some(mut pd) = data::get(c, pd.id) else { return 0 };
        if let Some(s) = pd.s_skill.as_mut() {
            if s.timer == 0 {
                let f = if s.id != 0 { Func::PetSkillSupportTimer } else { Func::PetHealTimer };
                s.timer = timer::add(c, tick + s.delay as Tick * 1000, f, aid, 0);
            }
        }
        if let Some(b) = pd.bonus.as_mut() {
            if b.timer == 0 {
                b.timer = timer::add(c, tick + b.delay as Tick * 1000, Func::PetSkillBonusTimer, aid, 0);
            }
        }
        put(c, &pd);
    }
    0
}

/// pet.cpp pet_unequipitem: the accessory comes off into the inventory (with no room it is destroyed:
/// pet_unequip_destroy); with pet_equip_required the skill bonus and support timers stop.
fn pet_unequipitem(c: &Ctx, aid: u32, pd_id: u32) -> i32 {
    let Some(pd) = data::get(c, pd_id) else { return 1 };
    if pd.pet.equip == 0 {
        return 1;
    }
    let nameid = pd.pet.equip;
    let mut tmp_item = Item::new(nameid);
    tmp_item.identify = 1;
    let flag = pc_additem(c, aid, &mut tmp_item, 1, crate::map_log::LOG_TYPE_OTHER);
    if flag != 0 {
        crate::clif_items::additem(c, aid, 0, 0, flag);
        // On official servers the item is destroyed if you don't have enough space
        if pet_unequip_destroy() {
            crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_OTHER, -1, &tmp_item);
        } else {
            // Don't unequip (and don't destroy) the item if failed to add it to the inventory
            return 1;
        }
    }
    let Some(mut pd) = data::get(c, pd_id) else { return 1 };
    pd.pet.equip = 0;
    put(c, &pd);
    status::status_set_viewdata(c, pd_id, pd.pet.class_ as i32);
    clif_send_petdata(c, None, &pd, CHANGESTATEPET_ACCESSORY);
    if pet_equip_required() {
        // Skotlex: halt support timers if needed
        let Some(mut pd) = data::get(c, pd_id) else { return 0 };
        if pd.skillbonus {
            pd.skillbonus = false;
            put(c, &pd);
            status_calc_pc(c, aid, SCO_NONE);
        }
        let Some(mut pd) = data::get(c, pd_id) else { return 0 };
        if let Some(s) = pd.s_skill.as_mut() {
            if s.timer != 0 {
                timer::delete(c, s.timer);
                s.timer = 0;
            }
        }
        if let Some(b) = pd.bonus.as_mut() {
            if b.timer != 0 {
                timer::delete(c, b.timer);
                b.timer = 0;
            }
        }
        put(c, &pd);
    }
    0
}

/// pet.cpp pet_food: the pet eats its food from the inventory (none: ZC_FEED_PET failure). Overfed (hunger above 90)
/// it loses IntimacyOverfed, else gains IntimacyFed (half when above 75); hunger rises by HungerIncrease.
pub fn pet_food(c: &Ctx, aid: u32, pd_id: u32) -> i32 {
    let Some(pd) = data::get(c, pd_id) else { return 1 };
    let Some(pdb) = pet_db_of(&pd) else { return 1 };
    let mut k = pdb.food_id as i32;
    let i = pc_search_inventory(c, aid, k as u32);
    if i < 0 {
        clif_pet_food(c, aid, k as u32, false);
        return 1;
    }
    pc_delitem(c, aid, i as i32, 1, 0, DELITEM_NORMAL, crate::map_log::LOG_TYPE_CONSUME);
    if pd.pet.hungry as i32 > PET_HUNGRY_SATISFIED {
        pet_set_intimate(c, pd_id, pd.pet.intimate as i32 + pdb.r_full);
        let Some(mut pd) = data::get(c, pd_id) else { return 1 };
        if pd.pet.intimate as i32 <= PET_INTIMATE_NONE {
            if let Some(mut u) = unit::get(c, pd_id) {
                unit::stop_attack(c, &mut u);
                unit::save(c, &u);
            }
            pd.status.speed = get_pet_walk_speed(c, &pd);
            put(c, &pd);
        }
    } else {
        k = if pet_friendly_rate() != 100 { pdb.r_hungry * pet_friendly_rate() / 100 } else { pdb.r_hungry };
        if pd.pet.hungry as i32 > PET_HUNGRY_NEUTRAL {
            k /= 2;
            k = k.max(1);
        }
        pet_set_intimate(c, pd_id, pd.pet.intimate as i32 + k);
    }
    status::status_calc_pet(c, pd_id, SCO_NONE);
    let Some(mut pd) = data::get(c, pd_id) else { return 1 };
    pd.pet.hungry = (pd.pet.hungry as i32 + pdb.hunger_increase).min(PET_HUNGRY_STUFFED) as i16;
    if pd.pet.hungry > 100 {
        pd.pet.hungry = 100;
    }
    put(c, &pd);
    crate::map_log::log_feeding(c, aid, crate::map_log::LOG_FEED_PET, pdb.food_id);
    clif_send_petdata(c, Some(aid), &pd, CHANGESTATEPET_HUNGER);
    clif_send_petdata(c, Some(aid), &pd, CHANGESTATEPET_INTIMACY);
    clif_pet_food(c, aid, pdb.food_id, true);
    0
}

/// pet.cpp pet_egg_search: the inventory index of the egg of pet `pet_id` (-1 = not found).
pub fn pet_egg_search(c: &Ctx, aid: u32, pet_id: u32) -> i32 {
    for r in crate::inventory::inv_list(c, aid) {
        if r.item.card0 == CARD0_PET && pet_id == egg_pet_id(&r.item) {
            return r.idx as i32;
        }
    }
    -1
}

// ---------------------------------------------------------------------------------------------- evolution

/// pet.cpp pet_evolution_requirements_check: the owner has every item the evolution into `pet_id` needs.
fn pet_evolution_requirements_check(c: &Ctx, aid: u32, pet_id: i16) -> bool {
    let Some(pd) = of_master(c, aid) else { return false };
    let Some(pdb) = pet_db_of(&pd) else { return false };
    let Some(evo) = pdb.evolution_data.iter().find(|e| e.target_mob_id as i32 == pet_id as u16 as i32) else { return false };
    let inv = crate::inventory::inv_list(c, aid);
    for &(nameid, amount) in &evo.requirements {
        let count: i32 = inv.iter().filter(|r| r.item.nameid == nameid).map(|r| r.item.amount as i32).sum();
        if count < amount as i32 {
            return false;
        }
    }
    true
}

/// pet.cpp pet_evolution (CZ_PET_EVOLUTION): a loyal pet without accessory evolves into `pet_id` for its evolution
/// items; its egg becomes the new egg (delete + add packets), the pet is shown again as the new monster.
pub fn pet_evolution(c: &Ctx, aid: u32, pet_id: i16) {
    if sd_of(c, aid).is_none() {
        return;
    }
    let Some(pd) = of_master(c, aid) else {
        clif_pet_evolution_result(c, aid, PET_EVOLUTION_FAIL_UNKNOWN);
        return;
    };
    if !feature_petevolution() {
        clif_pet_evolution_result(c, aid, PET_EVOLUTION_FAIL_UNKNOWN);
        return;
    }
    if (pd.pet.intimate as i32) < PET_INTIMATE_LOYAL {
        clif_pet_evolution_result(c, aid, PET_EVOLUTION_FAIL_RG_FAMILIAR);
        return;
    }
    if pd.pet.equip != 0 {
        clif_pet_evolution_result(c, aid, PET_EVOLUTION_FAIL_RG_FAMILIAR);
        return;
    }
    let Some(pdb) = pet_db_of(&pd) else { return };
    let Some(evo) = pdb.evolution_data.iter().find(|e| e.target_mob_id as i32 == pet_id as u16 as i32) else {
        clif_pet_evolution_result(c, aid, PET_EVOLUTION_FAIL_NOTEXIST_CALLPET);
        return;
    };
    if !pet_evolution_requirements_check(c, aid, pet_id) {
        clif_pet_evolution_result(c, aid, PET_EVOLUTION_FAIL_MATERIAL);
        return;
    }
    for &(nameid, amount) in &evo.requirements {
        let mut count = amount as i32;
        for i in 0..crate::inventory::MAX_INVENTORY {
            let Some(slot) = inv_get(c, aid, i) else { continue };
            let deduction = (amount as i32).min(slot.item.amount as i32);
            if slot.item.nameid == nameid {
                pc_delitem(c, aid, i as i32, deduction, 0, DELITEM_NORMAL, crate::map_log::LOG_TYPE_OTHER);
                count -= deduction;
                if count == 0 {
                    break;
                }
            }
        }
    }
    let Some(new_data) = pet_db().find(pet_id as u16) else { return };
    let Some(mut pd) = of_master(c, aid) else { return };
    let idx = pet_egg_search(c, aid, pd.pet.pet_id);
    if idx == -1 {
        clif_pet_evolution_result(c, aid, PET_EVOLUTION_FAIL_NOTEXIST_CALLPET);
        return;
    }
    // Virtually delete the old egg
    if let Some(r) = inv_get(c, aid, idx as usize) {
        crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_OTHER, -1, &r.item);
    }
    crate::clif_items::delitem(c, aid, idx, 1, 0);
    // Change the old egg to the new one
    if let Some(mut r) = inv_get(c, aid, idx as usize) {
        r.item.nameid = new_data.egg_id;
        inv_put(c, &r);
        // Virtually add it to the inventory
        crate::map_log::log_pick_pc(c, aid, crate::map_log::LOG_TYPE_OTHER, 1, &r.item);
    }
    // Virtually add it to the inventory
    crate::clif_items::additem(c, aid, idx, 1, 0);
    // Remove the old pet from sight
    unit::remove_map(c, pd.id, unit::CLR_OUTSIGHT);
    // Prepare the new pet
    pd.pet.class_ = pet_id;
    pd.pet.egg_id = new_data.egg_id;
    put(c, &pd);
    pet_set_intimate(c, pd.id, new_data.intimate as i32);
    let Some(mut pd) = data::get(c, pd.id) else { return };
    if pd.pet.rename_flag == 0 {
        if let Some(mdb) = crate::db::mobs::mob_db().find(pet_id as u16 as u32) {
            pd.pet.name = crate::int_pet::name_length(&mdb.jname);
        }
        put(c, &pd);
    }
    status::status_set_viewdata(c, pd.id, pet_id as i32);
    // Save the pet and inventory data
    intif_save_petdata(aid, &pd.pet);
    if CHARSAVE_PET {
        chrif_save_inventory(c, aid);
    }
    // Spawn it
    let Some(mut pu) = unit::get(c, pd.id) else { return };
    if !unit::addblock(c, &mut pu) {
        return;
    }
    crate::clif::spawn(c, &pu, false);
    let pd = data::get(c, pd.id).unwrap_or(pd);
    clif_send_petdata(c, Some(aid), &pd, CHANGESTATEPET_INIT);
    clif_send_petdata(c, Some(aid), &pd, CHANGESTATEPET_HAIRSTYLE);
    clif_send_petdata(c, None, &pd, CHANGESTATEPET_ACCESSORY);
    clif_send_petstatus(c, aid, &pd);
    if let Some(sdu) = unit::get(c, aid) {
        crate::clif::emotion(c, &sdu, ET_BEST);
    }
    if let Some(pu) = unit::get(c, pd.id) {
        crate::clif::specialeffect(c, &pu, EF_HO_UP, crate::clif::Target::Area);
    }
    clif_pet_evolution_result(c, aid, PET_EVOLUTION_SUCCESS);
    crate::clif_items::inventorylist(c, aid);
}

/// clif.hpp emotion_type ET_BEST / ET_OK; script_constants EF_HO_UP.
pub const ET_BEST: u8 = 21;
pub const ET_OK: u8 = 33;
pub const EF_HO_UP: i32 = 568;

// ---------------------------------------------------------------------------------------------- support bonuses

/// pet.cpp pet_clear_support_bonuses: the pet's attack / support skills, recovery, skill bonus and loot go (their
/// timers with them; the loot is given to the owner or dropped).
pub fn pet_clear_support_bonuses(c: &Ctx, aid: u32) {
    let Some(pd) = of_master(c, aid) else { return };
    let id = pd.id;
    let mut pd = pd;
    pd.a_skill = None;
    if let Some(s) = pd.s_skill.take() {
        if s.timer != 0 {
            timer::delete(c, s.timer);
        }
    }
    if let Some(r) = pd.recovery.take() {
        if r.timer != 0 {
            timer::delete(c, r.timer);
        }
    }
    if let Some(b) = pd.bonus.take() {
        if b.timer != 0 {
            timer::delete(c, b.timer);
        }
    }
    let has_loot = pd.loot.is_some();
    put(c, &pd);
    if has_loot {
        ai::pet_lootitem_drop(c, id, Some(aid));
        if let Some(mut pd) = data::get(c, id) {
            pd.loot = None;
            put(c, &pd);
        }
    }
}

/// pet.cpp PetDatabase::reload's map_foreachpc(pet_reload_sub) (@reloadpetdb): the pet database is embedded data, so
/// the "reload" reads the same entries; every player's pet then loses its support bonuses, runs its support script
/// again (pet_status_support), and the player's and the pet's status are recalculated.
pub fn pet_reload(c: &Ctx) {
    use crate::pc::pc as _;
    let mut pcs: Vec<u32> = c.db().pc().iter().map(|p| p.account_id).collect();
    pcs.sort_by_key(|&aid| crate::party::idb_order(aid));
    for aid in pcs {
        pet_reload_sub(c, aid);
    }
}

/// pet.cpp pet_reload_sub.
fn pet_reload_sub(c: &Ctx, aid: u32) -> i32 {
    let Some(pd) = of_master(c, aid) else { return 0 };
    let Some(pet_db_ptr) = pet_db_of(&pd) else { return 0 };
    // Clear pet bonuses
    pet_clear_support_bonuses(c, aid);
    // Relink the pet to the new database entry (pd->db = mob_db.find(pet_db_ptr->class_))
    if let Some(mut pd) = data::get(c, pd.id) {
        pd.db_mob_id = pet_db_ptr.class_ as u32;
        put(c, &pd);
    }
    if pet_status_support() {
        bonus::run_support_script(c, aid, pet_db_ptr);
    }
    // Recalculate the player status based on the new data
    status_calc_pc(c, aid, SCO_NONE);
    // Recalculate the pet status based on the new data
    status::status_calc_pet(c, pd.id, SCO_NONE);
    0
}

// ---------------------------------------------------------------------------------------------- the owner's life

/// pc.cpp pc_reg_received: `if (sd->status.pet_id > 0) intif_request_petdata(…)`, and the char server's answer
/// (mapif_load_pet → pet_recv_petdata). rAthena gets that answer after the storage answers and before the skill
/// cooldowns, bonus scripts, mail, quests, achievements and status changes; the module's login handles those answers
/// in place in that order (pc.rs authok), so the pet's request is answered in place too, at its turn.
pub fn pc_reg_received(c: &Ctx, aid: u32) {
    let pet_id = pet_id_of(c, aid);
    if pet_id == 0 {
        return;
    }
    let Some(p) = sd_of(c, aid) else { return };
    if let Inter::PetInfo { account_id, pet, flag } = crate::int_pet::mapif_load_pet_answer(c, aid, p.char_id, pet_id) {
        pet_recv_petdata(c, account_id, pet, flag);
    }
}

/// clif.cpp clif_parse_LoadEndAck, the pet part after the area's units (clif.cpp:10852-10865): the pet enters the map
/// with its owner (a GvG map with pet_disable_in_gvg returns it to its egg instead).
pub fn load_end_ack(c: &Ctx, aid: u32) {
    let Some(pd) = of_master(c, aid) else { return };
    let Some(sdu) = unit::get(c, aid) else { return };
    if pet_no_gvg() && crate::npc::mapflag_gvg(c, sdu.map) {
        //Return the pet to egg. [Skotlex]
        crate::clif::displaymessage(c, &sdu, crate::data::msg_txt(666));
        pet_return_egg(c, aid, pd.id);
        return;
    }
    let Some(mut pu) = unit::get(c, pd.id) else { return };
    if !unit::addblock(c, &mut pu) {
        return;
    }
    crate::clif::spawn(c, &pu, false);
    clif_send_petdata(c, Some(aid), &pd, CHANGESTATEPET_INIT);
    clif_send_petstatus(c, aid, &pd);
}

/// clif.cpp clif_parse_LoadEndAck for a new connection (clif.cpp:10939-10940): a pet of intimacy above 900 greets
/// its owner (ZC_PET_ACT with the talk table's "connect" entry).
pub fn load_end_ack_connect_new(c: &Ctx, aid: u32) {
    if let Some(pd) = of_master(c, aid) {
        if pd.pet.intimate > 900 {
            clif_pet_emotion(c, &pd, (pd.pet.class_ as i32 - 100) * 100 + 50 + pet_hungry_val(&pd));
        }
    }
}

/// pc.cpp pc_setpos (pc.cpp:7134-7140): a pet that is still friendly moves with its owner (it re-enters the map at the
/// owner's LoadEndAck).
pub fn pc_setpos(c: &Ctx, aid: u32, m: u16, x: i16, y: i16) {
    if pet_id_of(c, aid) == 0 {
        return;
    }
    let Some(pd) = of_master(c, aid) else { return };
    if pd.pet.intimate <= 0 {
        return;
    }
    let dir = unit::get(c, aid).map(|u| u.ud.dir).unwrap_or(0);
    if let Some(mut pu) = unit::get(c, pd.id) {
        pu.map = m;
        pu.x = x;
        pu.ud.to_x = x;
        pu.y = y;
        pu.ud.to_y = y;
        pu.ud.dir = dir;
        unit::save(c, &pu);
    }
}

/// unit.cpp unit_remove_map_pc's pet part: after the owner, the pet leaves the map too (a warp or logout's warp
/// animation becomes a plain vanish for it).
pub fn unit_remove_map_pc(c: &Ctx, aid: u32, clrtype: u8) {
    //CLR_RESPAWN is the warp from logging out, CLR_TELEPORT is the warp from teleporting, but pets/homunc need to just
    //'vanish' instead of showing the warping animation.
    let clrtype = if clrtype == unit::CLR_RESPAWN || clrtype == unit::CLR_TELEPORT { unit::CLR_OUTSIGHT } else { clrtype };
    if let Some(pd) = of_master(c, aid) {
        unit::remove_map(c, pd.id, clrtype);
    }
}

/// map.cpp map_quit: "Return loot to owner" before unit_remove_map_pc.
pub fn map_quit_loot(c: &Ctx, aid: u32) {
    if let Some(pd) = of_master(c, aid) {
        ai::pet_lootitem_drop(c, pd.id, Some(aid));
    }
}

/// unit.cpp unit_free_pc's pet part: the pet is freed (saved, or deleted when intimacy is gone).
pub fn unit_free_pc(c: &Ctx, aid: u32) {
    if let Some(pd) = of_master(c, aid) {
        ai::unit_free(c, pd.id, unit::CLR_OUTSIGHT);
    }
}

/// pc.cpp pc_dead (pc.cpp:9839-9847): the owner died; unless the map has noexppenalty, the pet loses
/// IntimacyOwnerDie (ZC_CHANGESTATE_PET intimacy), and it drops its target.
pub fn pc_dead(c: &Ctx, aid: u32) {
    if pet_id_of(c, aid) == 0 {
        return;
    }
    let Some(pd) = of_master(c, aid) else { return };
    let m = unit::get(c, aid).map(|u| u.map).unwrap_or(0);
    if !crate::npc::mapflag(c, m, crate::npc::MF_NOEXPPENALTY) {
        let die = pet_db_of(&pd).map(|p| p.die).unwrap_or(0);
        pet_set_intimate(c, pd.id, pd.pet.intimate as i32 + die);
        if let Some(pd) = data::get(c, pd.id) {
            clif_send_petdata(c, Some(aid), &pd, CHANGESTATEPET_INTIMACY);
        }
    }
    if data::get(c, pd.id).map(|p| p.target_id != 0).unwrap_or(false) {
        // Unlock all targets...
        pet_unlocktarget(c, pd.id);
    }
}

/// pc.cpp pc_damage (pc.cpp:9700-9701): with pet_damage_support the pet may turn on whoever hurt its owner.
pub fn pc_damage(c: &Ctx, aid: u32, src: Option<&unit::Unit>) {
    if !pet_damage_support() || pet_id_of(c, aid) == 0 {
        return;
    }
    if let Some(pd) = of_master(c, aid) {
        pet_target_check(c, pd.id, src, 1);
    }
}

/// unit.cpp unit_attack_timer_sub (unit.cpp:3311-3312): with pet_attack_support the pet may join its owner's attack.
pub fn unit_attack_support(c: &Ctx, aid: u32, target: &unit::Unit) {
    if !pet_attack_support() || pet_id_of(c, aid) == 0 {
        return;
    }
    if let Some(pd) = of_master(c, aid) {
        pet_target_check(c, pd.id, Some(target), 0);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn egg_cards() {
        // pet.cpp pet_get_card3_intimacy: the level boundaries
        assert_eq!([0, 99, 100, 249, 250, 749, 750, 909, 910, 1000, 1001].map(pet_get_card3_intimacy), [2, 2, 4, 4, 6, 6, 8, 8, 10, 10, 0]);
        // MakeDWord(card[1], card[2]) is the inverse of GetWord(pet_id, 0) / GetWord(pet_id, 1)
        let mut it = Item::new(9001);
        it.card0 = CARD0_PET;
        it.card1 = 0x0001_2345 & 0xFFFF;
        it.card2 = (0x0001_2345u32 >> 16) & 0xFFFF;
        assert_eq!(egg_pet_id(&it), 0x0001_2345);
        // a PORING egg (db/re/pet_db.yml: IntimacyStart 250, Neutral)
        let pet = pet_db_search(9001, PetItemType::Egg).expect("Poring egg");
        assert_eq!(pet_get_card3_intimacy(pet.intimate as i32), 3 << 1);
    }

    #[test]
    fn percentage_and_names() {
        // utils.cpp get_percentage works in double: 29 / 100 is 28.999999999999996 → 28
        assert_eq!(get_percentage(29, 100), 28);
        assert_eq!(get_percentage(50, 100), 50);
        assert_eq!(get_percentage(100, 100), 100);
        assert_eq!(get_percentage(0, 55), 0);
        assert_eq!(get_percentage(1, 0), u32::MAX);
        // strlib.cpp normalize_name(" ")
        assert_eq!(normalize_name(b"  My   Pet  "), b"My Pet".to_vec());
        assert_eq!(normalize_name(b"   "), Vec::<u8>::new());
        assert_eq!(normalize_name(b"Po\0ring"), b"Po".to_vec());
    }

    #[test]
    fn hunger_levels() {
        let mut pd = PetData {
            id: 1,
            master: 1,
            pet: SPet::default(),
            db_mob_id: 1002,
            status: Default::default(),
            pet_hungry_timer: 0,
            target_id: 0,
            skillbonus: false,
            move_fail_count: 0,
            next_walktime: 0,
            last_thinktime: 0,
            rate_fix: 0,
            recovery: None,
            bonus: None,
            a_skill: None,
            s_skill: None,
            loot: None,
            autobonus: vec![],
            autobonus2: vec![],
            autobonus3: vec![],
            masterteleport_timer: 0,
        };
        for (h, v) in [(100, 4), (91, 4), (90, 3), (76, 3), (75, 2), (26, 2), (25, 1), (11, 1), (10, 0), (0, 0)] {
            pd.pet.hungry = h;
            assert_eq!(pet_hungry_val(&pd), v, "hungry {h}");
        }
    }
}
