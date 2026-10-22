//! mercenary.hpp struct s_mercenary_data as a table: one `mercenary_data` row per mercenary that exists on the map
//! server (sd->md), next to the mercenary's `unit` row (its block_list / unit_data / view_data, kind BL_MER), as a
//! monster's mob_data sits next to its unit row. The row lives from mercenary_recv_data (map_addiddb) to unit_free.
//! The owner's mercenary fields of mmo_charstatus (mer_id and the guild counters) are the map-side `pc_mercenary` row
//! (loaded from the char server's `mercenary_owner` at login, written back at every save).

use spacetimedb::{table, SpacetimeType, Table};

use crate::int_mercenary::SMercenary;
use crate::status::data::{RegenData, StatusData};
use crate::world::{Ctx, Tick};

/// mercenary.hpp MAX_MER_DISTANCE: cells a mercenary can be away from its master before being warped.
pub const MAX_MER_DISTANCE: i32 = 15;

/// mercenary.hpp enum e_MercGuildType.
pub const NONE_MERC_GUILD: i32 = -1;
pub const ARCH_MERC_GUILD: i32 = 0;
pub const SPEAR_MERC_GUILD: i32 = 1;
pub const SWORD_MERC_GUILD: i32 = 2;

/// mercenary.hpp enum e_MERID.
pub const MERID_MER_ARCHER01: i32 = 6017;
pub const MERID_MER_ARCHER10: i32 = 6026;
pub const MERID_MER_LANCER01: i32 = 6027;
pub const MERID_MER_LANCER10: i32 = 6036;
pub const MERID_MER_SWORDMAN01: i32 = 6037;
pub const MERID_MER_SWORDMAN10: i32 = 6046;

/// One entry of md->scd (skill id -> the skill_blockmerc_end timer).
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct MercScd {
    pub skill_id: u16,
    pub timer: u64,
}

/// mercenary.hpp struct s_mercenary_data less its block_list / unit_data / view_data (the `unit` row). Timer fields
/// hold timer ids (0 = INVALID_TIMER).
#[table(accessor = mercenary_data)]
#[derive(Clone, Debug)]
pub struct MercenaryData {
    /// block_list id (npc_get_new_npc_id).
    #[primary_key]
    pub id: u32,
    /// md->master: the owner's account id (sd->md is the row whose master is sd).
    #[unique]
    pub master: u32,
    /// md->db: mercenary_db.find(class_) (the class the data was received with).
    pub db_class: i32,
    /// md->mercenary (mmo.hpp s_mercenary).
    pub mercenary: SMercenary,
    pub base_status: StatusData,
    pub battle_status: StatusData,
    pub regen: RegenData,
    /// regen.tick.hp / .sp.
    pub regen_tick_hp: Tick,
    pub regen_tick_sp: Tick,
    /// md->scd (std::unordered_map<uint16, int32>): the entries, and the map's iteration-order state (skill/umap.rs).
    pub scd: Vec<MercScd>,
    pub scd_order: Vec<u16>,
    pub scd_buckets: u64,
    pub scd_next_resize: u64,
    pub masterteleport_timer: u64,
    pub contract_timer: u64,
    pub devotion_flag: bool,
}

impl MercenaryData {
    /// md->scd's iteration-order state.
    pub fn scd_umap(&self) -> crate::skill::umap::Umap {
        crate::skill::umap::Umap { order: self.scd_order.clone(), buckets: self.scd_buckets, next_resize: self.scd_next_resize }
    }
    pub fn set_scd_umap(&mut self, m: crate::skill::umap::Umap) {
        self.scd_order = m.order;
        self.scd_buckets = m.buckets;
        self.scd_next_resize = m.next_resize;
    }
    /// md->scd in the unordered_map's iteration order.
    pub fn scd_entries(&self) -> Vec<MercScd> {
        self.scd_order.iter().filter_map(|k| self.scd.iter().find(|e| e.skill_id == *k).cloned()).collect()
    }
}

/// The mercenary fields of the owner's mmo_charstatus (sd->status.mer_id, arch_calls … sword_faith), for a character
/// in the world.
#[table(accessor = pc_mercenary)]
#[derive(Clone, Debug, Default, PartialEq)]
pub struct PcMercenary {
    #[primary_key]
    pub account_id: u32,
    pub char_id: u32,
    pub mer_id: i32,
    pub arch_calls: i32,
    pub arch_faith: i32,
    pub spear_calls: i32,
    pub spear_faith: i32,
    pub sword_calls: i32,
    pub sword_faith: i32,
}

// ---------------------------------------------------------------------------------------------- access

/// A mercenary by its block id (map_id2mc).
pub fn get(c: &Ctx, id: u32) -> Option<MercenaryData> {
    c.db().mercenary_data().id().find(id)
}

/// sd->md: the mercenary of the player with account id `aid`.
pub fn of_master(c: &Ctx, aid: u32) -> Option<MercenaryData> {
    c.db().mercenary_data().master().find(aid)
}

/// Saves the row and the unit row's copies of the status (speed, mode, HP, level, name).
pub fn put(c: &Ctx, md: &MercenaryData) {
    if c.db().mercenary_data().id().find(md.id).is_some() {
        c.db().mercenary_data().id().update(md.clone());
    } else {
        c.db().mercenary_data().insert(md.clone());
    }
    crate::companion::sync_unit(c, md.id, &md.battle_status, db_of(md).map(|d| d.lv as i32).unwrap_or(1), db_of(md).map(|d| d.name.as_str()).unwrap_or(""));
}

/// md->db.
pub fn db_of(md: &MercenaryData) -> Option<&'static crate::db::misc::mercenary::MercenaryDb> {
    crate::db::misc::mercenary::mercenary_db().find(md.db_class)
}

/// sd->status.mer_id / guild counters of a player in the world (a zeroed row when none was loaded).
pub fn owner(c: &Ctx, aid: u32) -> PcMercenary {
    c.db().pc_mercenary().account_id().find(aid).unwrap_or(PcMercenary { account_id: aid, ..Default::default() })
}

pub fn put_owner(c: &Ctx, o: &PcMercenary) {
    match c.db().pc_mercenary().account_id().find(o.account_id) {
        Some(old) if old == *o => {}
        Some(_) => {
            c.db().pc_mercenary().account_id().update(o.clone());
        }
        None => {
            c.db().pc_mercenary().insert(o.clone());
        }
    }
}

/// sd->status.mer_id.
pub fn mer_id_of(c: &Ctx, aid: u32) -> i32 {
    c.db().pc_mercenary().account_id().find(aid).map(|o| o.mer_id).unwrap_or(0)
}

/// sd->status.mer_id = v.
pub fn set_mer_id(c: &Ctx, aid: u32, v: i32) {
    let mut o = owner(c, aid);
    o.mer_id = v;
    put_owner(c, &o);
}
