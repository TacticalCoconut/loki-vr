//! pet.hpp struct pet_data as a table: one `pet_data` row per pet that is out of its egg (sd->pd), next to the pet's
//! `unit` row (its block_list / unit_data / view_data, kind BL_PET), exactly as mob_data sits next to a monster's
//! unit row. The row lives from pet_data_init to unit_free, on the map or not (map_addiddb / map_deliddb).

use spacetimedb::{table, SpacetimeType, Table};

use crate::int_pet::SPet;
use crate::inventory::Item;
use crate::status::data::StatusData;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// pet.hpp MAX_PETLOOT_SIZE.
pub const MAX_PETLOOT_SIZE: i32 = 30;
/// pet.cpp MIN_PETTHINKTIME.
pub const MIN_PETTHINKTIME: Tick = 100;

/// pet.hpp enum e_pet_intimate_level.
pub const PET_INTIMATE_NONE: i32 = 0;
pub const PET_INTIMATE_AWKWARD: i32 = 1;
pub const PET_INTIMATE_SHY: i32 = 100;
pub const PET_INTIMATE_NEUTRAL: i32 = 250;
pub const PET_INTIMATE_CORDIAL: i32 = 750;
pub const PET_INTIMATE_LOYAL: i32 = 910;
pub const PET_INTIMATE_MAX: i32 = 1000;

/// pet.hpp enum e_pet_hungry.
pub const PET_HUNGRY_NONE: i32 = 0;
pub const PET_HUNGRY_VERY_HUNGRY: i32 = 10;
pub const PET_HUNGRY_HUNGRY: i32 = 25;
pub const PET_HUNGRY_NEUTRAL: i32 = 75;
pub const PET_HUNGRY_SATISFIED: i32 = 90;
pub const PET_HUNGRY_STUFFED: i32 = 100;

/// pet.hpp enum e_pet_catch_flag.
pub const PET_CATCH_NORMAL: u8 = 0;
pub const PET_CATCH_UNIVERSAL_NO_BOSS: u8 = 1;
pub const PET_CATCH_UNIVERSAL_ALL: u8 = 2;
pub const PET_CATCH_MAX: u8 = 3;

/// pet.hpp struct pet_recovery (Stat recovery): the status change it cures, the delay (s) and the timer.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct PetRecovery {
    pub sc_type: i32,
    pub delay: u16,
    pub timer: u64,
}

/// pet.hpp struct pet_bonus (bStr, bVit?…): the bonus, its value, duration and delay (s) and the timer.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct PetBonus {
    pub bonus_type: u16,
    pub val: u16,
    pub duration: u16,
    pub delay: u16,
    pub timer: u64,
}

/// pet.hpp struct pet_skill_attack.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct PetSkillAttack {
    pub id: u16,
    /// Skill level
    pub lv: u16,
    /// Fixed damage value of petskillattack2
    pub damage: u16,
    /// 0 = Normal skill. >0 = Fixed damage (lv), fixed div_.
    pub div_: u16,
    /// Base chance of skill ocurrance (10 = 10% of attacks)
    pub rate: u16,
    /// How being 100% loyal affects cast rate (10 = At 1000 intimacy->rate+10%
    pub bonusrate: u16,
}

/// pet.hpp struct pet_skill_support.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct PetSkillSupport {
    pub id: u16,
    pub lv: u16,
    /// Max HP% for skill to trigger (50 -> 50% for Magnificat)
    pub hp: u16,
    /// Max SP% for skill to trigger (100 = no check)
    pub sp: u16,
    /// Time (secs) between being able to recast.
    pub delay: u16,
    pub timer: u64,
}

/// pet.hpp struct pet_loot: `max` slots, the first `count` of them used (`items` holds exactly those).
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct PetLoot {
    pub items: Vec<Item>,
    pub weight: u16,
    pub max: u16,
}

impl PetLoot {
    pub fn count(&self) -> u16 {
        self.items.len() as u16
    }
}

/// pet.hpp struct s_petautobonus (`timer` 0 = INVALID_TIMER).
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct PetAutobonus {
    pub rate: i16,
    pub atk_type: u16,
    pub bonus_script: String,
    pub other_script: String,
    pub duration: i64,
    pub timer: u64,
}

/// pet.hpp struct pet_data less its block_list / unit_data / view_data (the `unit` row). Timer fields hold timer ids
/// (0 = INVALID_TIMER).
#[table(accessor = pet_data)]
#[derive(Clone, Debug)]
pub struct PetData {
    /// block_list id (npc_get_new_npc_id).
    #[primary_key]
    pub id: u32,
    /// pd->master: the owner's account id (map_session_data; sd->pd is the row whose master is sd).
    #[unique]
    pub master: u32,
    /// pd->pet (mmo.hpp s_pet).
    pub pet: SPet,
    /// pd->db: mob_db.find(pet->class_) as pet_data_init found it. rAthena never points it at another monster: an
    /// evolution changes pet.class_ but keeps the old monster's database entry here (its ranges, boss type, speed).
    pub db_mob_id: u32,
    /// pd->status.
    pub status: StatusData,
    pub pet_hungry_timer: u64,
    pub target_id: u32,
    /// pd->state.skillbonus.
    pub skillbonus: bool,
    pub move_fail_count: i32,
    pub next_walktime: Tick,
    pub last_thinktime: Tick,
    /// Support rate as modified by intimacy (1000 = 100%) [Skotlex]
    pub rate_fix: u16,
    pub recovery: Option<PetRecovery>,
    pub bonus: Option<PetBonus>,
    pub a_skill: Option<PetSkillAttack>,
    pub s_skill: Option<PetSkillSupport>,
    pub loot: Option<PetLoot>,
    pub autobonus: Vec<PetAutobonus>,
    pub autobonus2: Vec<PetAutobonus>,
    pub autobonus3: Vec<PetAutobonus>,
    pub masterteleport_timer: u64,
}

/// pet.cpp pet_catchprocesses: the capture a player started (pet_catch_process_start), by char id. rAthena keeps the
/// entries in memory until the capture ends (a player who logs out keeps his until his next capture).
#[table(accessor = pet_catch_process)]
#[derive(Clone, Debug)]
pub struct PetCatchProcess {
    #[primary_key]
    pub char_id: u32,
    pub taming_item: u32,
    pub flag: u8,
}

/// pet.cpp pet_delayed_drops: the loot a pet drops, by the pet's block id (pet_lootitem_drop → pet_delay_item_drop,
/// 500 ms later). A second drop of the same pet before the first timer replaces the list, as the unordered_map
/// assignment does.
#[table(accessor = pet_delayed_drop)]
#[derive(Clone, Debug)]
pub struct PetDelayedDrop {
    #[primary_key]
    pub id: u32,
    pub m: u16,
    pub x: i16,
    pub y: i16,
    pub items: Vec<Item>,
}

/// The pet AI timer (do_init_pet's add_timer_interval).
#[table(accessor = pet_ai_state)]
pub struct PetAiState {
    #[primary_key]
    pub key: u8,
    pub timer: u64,
}

// ---------------------------------------------------------------------------------------------- access

/// A pet by its block id (map_id2pd).
pub fn get(c: &Ctx, id: u32) -> Option<PetData> {
    c.db().pet_data().id().find(id)
}

/// sd->pd: the pet of the player with account id `aid`.
pub fn of_master(c: &Ctx, aid: u32) -> Option<PetData> {
    c.db().pet_data().master().find(aid)
}

/// Saves the row and keeps the unit row's copies of what clif and the walking code read (name, speed, mode, level,
/// HP, the boss type of the unit packets).
pub fn put(c: &Ctx, pd: &PetData) {
    if c.db().pet_data().id().find(pd.id).is_some() {
        c.db().pet_data().id().update(pd.clone());
    } else {
        c.db().pet_data().insert(pd.clone());
    }
    sync_unit(c, pd);
}

/// The unit row mirrors pd->pet.name (clif_name, the unit packets), pd->status.speed / mode / hp (status_get_speed,
/// status_bl_has_mode) and pd->db's boss type (clif_set_unit_idle / clif_spawn_unit: `pd->db->get_bosstype()`).
pub fn sync_unit(c: &Ctx, pd: &PetData) {
    let Some(mut u) = unit::get(c, pd.id) else { return };
    let boss = crate::mob::db_find(c, pd.db_mob_id as i32).map(|d| d.get_bosstype() as u8).unwrap_or(0);
    let (hp, max_hp) = (pd.status.hp as i64, pd.status.max_hp as i64);
    if u.name != pd.pet.name || u.speed != pd.status.speed || u.mode != pd.status.mode as u32 || u.level != pd.pet.level as i32 || u.boss != boss || u.hp != hp || u.max_hp != max_hp || u.size != pd.status.size {
        u.name = pd.pet.name.clone();
        u.speed = pd.status.speed;
        u.mode = pd.status.mode as u32;
        u.level = pd.pet.level as i32;
        u.boss = boss;
        u.hp = hp;
        u.max_hp = max_hp;
        u.size = pd.status.size;
        unit::save(c, &u);
    }
}

/// The pet's unit row (block_list / unit_data / view_data).
pub fn unit_of(c: &Ctx, pd: &PetData) -> Option<Unit> {
    unit::get(c, pd.id)
}

/// pd->get_pet_db(): pet_db.find(pd->pet.class_).
pub fn pet_db_of(pd: &PetData) -> Option<&'static crate::db::misc::pet::PetDb> {
    crate::db::misc::pet::pet_db().find(pd.pet.class_ as u16)
}

/// pd->db: the monster database entry of the pet (see [`PetData::db_mob_id`]).
pub fn mob_db_of(c: &Ctx, pd: &PetData) -> Option<&'static crate::db::mobs::MobDb> {
    crate::mob::db_find(c, pd.db_mob_id as i32)
}

/// conf/battle/pet.conf `pet_walk_speed: 1`.
pub fn pet_walk_speed() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::PET_WALK_SPEED)
}

/// pet.hpp pet_data::get_pet_walk_speed: 1 the master's speed, 2 DEFAULT_WALK_SPEED, 3 the monster database's.
pub fn get_pet_walk_speed(c: &Ctx, pd: &PetData) -> u16 {
    match pet_walk_speed() {
        2 => crate::status::defs::DEFAULT_WALK_SPEED as u16,
        3 => mob_db_of(c, pd).map(|d| d.status.speed).unwrap_or(crate::status::defs::DEFAULT_WALK_SPEED as u16),
        // default / 1: Master
        _ => crate::status::data::sd_get(c, pd.master).map(|sd| sd.battle_status.speed).unwrap_or(crate::status::defs::DEFAULT_WALK_SPEED as u16),
    }
}
