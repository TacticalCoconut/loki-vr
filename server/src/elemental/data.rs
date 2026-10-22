//! elemental.hpp struct s_elemental_data as a table: one `elemental_data` row per elemental that exists on the map
//! server (sd->ed), next to its `unit` row (block_list / unit_data / view_data, kind BL_ELEM). The row lives from
//! elemental_data_received (map_addiddb) to unit_free. sd->status.ele_id is the character row's `elemental_id`.

use spacetimedb::{table, Table};

use crate::int_elemental::SElemental;
use crate::status::data::{RegenData, StatusData};
use crate::world::{Ctx, Tick};

/// elemental.hpp MIN_ELETHINKTIME / MIN_ELEDISTANCE / MAX_ELEDISTANCE.
pub const MIN_ELETHINKTIME: Tick = 100;
pub const MIN_ELEDISTANCE: i32 = 2;
pub const MAX_ELEDISTANCE: i32 = 5;

/// elemental.hpp EL_MODE_AGGRESSIVE / EL_MODE_ASSIST / EL_MODE_PASSIVE (mob.hpp e_mode bits).
pub const EL_MODE_AGGRESSIVE: i32 = crate::db::mobs::MD_CANMOVE | crate::db::mobs::MD_AGGRESSIVE | crate::db::mobs::MD_CANATTACK;
pub const EL_MODE_ASSIST: i32 = crate::db::mobs::MD_CANMOVE | crate::db::mobs::MD_ASSIST;
pub const EL_MODE_PASSIVE: i32 = crate::db::mobs::MD_CANMOVE;

/// elemental.hpp enum e_elemental_skillmode.
pub use crate::db::misc::elemental::{EL_SKILLMODE_AGGRESSIVE, EL_SKILLMODE_ASSIST, EL_SKILLMODE_PASSIVE};

/// elemental.hpp enum elemental_elementalid.
pub const ELEMENTALID_AGNI_S: i32 = 2114;
pub const ELEMENTALID_AGNI_M: i32 = 2115;
pub const ELEMENTALID_AGNI_L: i32 = 2116;
pub const ELEMENTALID_AQUA_S: i32 = 2117;
pub const ELEMENTALID_AQUA_M: i32 = 2118;
pub const ELEMENTALID_AQUA_L: i32 = 2119;
pub const ELEMENTALID_VENTUS_S: i32 = 2120;
pub const ELEMENTALID_VENTUS_M: i32 = 2121;
pub const ELEMENTALID_VENTUS_L: i32 = 2122;
pub const ELEMENTALID_TERA_S: i32 = 2123;
pub const ELEMENTALID_TERA_M: i32 = 2124;
pub const ELEMENTALID_TERA_L: i32 = 2125;
pub const ELEMENTALID_DILUVIO: i32 = 20816;
pub const ELEMENTALID_ARDOR: i32 = 20817;
pub const ELEMENTALID_PROCELLA: i32 = 20818;
pub const ELEMENTALID_TERREMOTUS: i32 = 20819;
pub const ELEMENTALID_SERPENS: i32 = 20820;

/// elemental.hpp struct s_elemental_data less its block_list / unit_data / view_data (the `unit` row). Timer fields
/// hold timer ids (0 = INVALID_TIMER).
#[table(accessor = elemental_data)]
#[derive(Clone, Debug)]
pub struct ElementalData {
    /// block_list id (npc_get_new_npc_id).
    #[primary_key]
    pub id: u32,
    /// ed->master: the owner's account id.
    #[unique]
    pub master: u32,
    /// ed->db: elemental_db.find(class_).
    pub db_class: i32,
    /// ed->elemental (mmo.hpp s_elemental).
    pub elemental: SElemental,
    pub base_status: StatusData,
    pub battle_status: StatusData,
    pub regen: RegenData,
    /// regen.tick.hp / .sp.
    pub regen_tick_hp: Tick,
    pub regen_tick_sp: Tick,
    pub masterteleport_timer: u64,
    pub summon_timer: u64,
    pub skill_timer: u64,
    pub last_thinktime: Tick,
    pub last_linktime: Tick,
    pub last_spdrain_time: Tick,
    pub min_chase: i16,
    pub target_id: u32,
    pub attacked_id: u32,
    /// elemental_delete's `sd->ed = nullptr` happened (the row goes at unit_free; ed->master still names the player).
    pub detached: bool,
}

/// The elemental AI timer (do_init_elemental's add_timer_interval).
#[table(accessor = elemental_ai_state)]
pub struct ElementalAiState {
    #[primary_key]
    pub key: u8,
    pub timer: u64,
}

// ---------------------------------------------------------------------------------------------- access

/// An elemental by its block id (map_id2ed).
pub fn get(c: &Ctx, id: u32) -> Option<ElementalData> {
    c.db().elemental_data().id().find(id)
}

/// sd->ed: the elemental of the player with account id `aid`.
pub fn of_master(c: &Ctx, aid: u32) -> Option<ElementalData> {
    c.db().elemental_data().master().find(aid).filter(|ed| !ed.detached)
}

/// ed->db.
pub fn db_of(ed: &ElementalData) -> Option<&'static crate::db::misc::elemental::ElementalDb> {
    crate::db::misc::elemental::elemental_db().find(ed.db_class)
}

/// Saves the row and the unit row's copies of the status (speed, mode, HP, level, name).
pub fn put(c: &Ctx, ed: &ElementalData) {
    if c.db().elemental_data().id().find(ed.id).is_some() {
        c.db().elemental_data().id().update(ed.clone());
    } else {
        c.db().elemental_data().insert(ed.clone());
    }
    crate::companion::sync_unit(c, ed.id, &ed.battle_status, db_of(ed).map(|d| d.lv as i32).unwrap_or(1), db_of(ed).map(|d| d.name.as_str()).unwrap_or(""));
}

/// sd->status.ele_id (the character row's `elemental_id`).
pub fn ele_id_of(c: &Ctx, aid: u32) -> u32 {
    crate::pc::character_of(c, aid).map(|ch| ch.elemental_id).unwrap_or(0)
}

/// sd->status.ele_id = v.
pub fn set_ele_id(c: &Ctx, aid: u32, v: u32) {
    if let Some(mut ch) = crate::pc::character_of(c, aid) {
        if ch.elemental_id != v {
            ch.elemental_id = v;
            crate::status::put_char(c, &ch);
        }
    }
}
