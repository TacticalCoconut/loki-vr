//! homunculus.hpp struct homun_data as a table: one `hom_data` row per homunculus a player has loaded (sd->hd), next
//! to the homunculus' `unit` row (its block_list / unit_data / view_data, kind BL_HOM), exactly as pet_data sits next
//! to a pet's unit row. The row lives from hom_alloc to unit_free, on the map or not (map_addiddb / map_deliddb); a
//! vaporized or dead homunculus keeps its row (off the map) until its master leaves.

use spacetimedb::{table, Table};

use crate::int_homun::SHomunculus;
use crate::status::data::{RegenData, StatusData};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// homunculus.hpp enum e_hom_state (s_homunculus::vaporize).
pub const HOM_ST_ACTIVE: i32 = 0;
pub const HOM_ST_REST: i32 = 1;
pub const HOM_ST_MORPH: i32 = 2;

/// homunculus.hpp enum e_hom_state2 (clif_send_homdata).
pub const SP_ACK: u8 = 0x0;
pub const SP_INTIMATE: u8 = 0x1;
pub const SP_HUNGRY: u8 = 0x2;

/// homunculus.hpp enum e_hom_mode (SC_STYLE_CHANGE's val1).
pub const MH_MD_FIGHTING: i32 = 1;
pub const MH_MD_GRAPPLING: i32 = 2;

/// homunculus.hpp HOM_EVO / HOM_S / HOM_REG (the homun_mapid bits).
pub const HOM_EVO: i32 = 0x100;
pub const HOM_S: i32 = 0x200;
pub const HOM_REG: i32 = 0x1000;

/// homunculus.hpp enum homun_mapid.
pub const MAPID_LIF: i32 = HOM_REG;
pub const MAPID_AMISTR: i32 = HOM_REG | 1;
pub const MAPID_FILIR: i32 = HOM_REG | 2;
pub const MAPID_VANILMIRTH: i32 = HOM_REG | 3;
pub const MAPID_LIF_E: i32 = HOM_REG | HOM_EVO;
pub const MAPID_AMISTR_E: i32 = HOM_REG | HOM_EVO | 1;
pub const MAPID_FILIR_E: i32 = HOM_REG | HOM_EVO | 2;
pub const MAPID_VANILMIRTH_E: i32 = HOM_REG | HOM_EVO | 3;
pub const MAPID_EIRA: i32 = HOM_S;
pub const MAPID_BAYERI: i32 = HOM_S | 1;
pub const MAPID_SERA: i32 = HOM_S | 2;
pub const MAPID_DIETER: i32 = HOM_S | 3;
pub const MAPID_ELANOR: i32 = HOM_S | 4;

/// homunculus.hpp enum e_homun_classid.
pub const MER_LIF: i32 = 6001;
pub const MER_AMISTR: i32 = 6002;
pub const MER_FILIR: i32 = 6003;
pub const MER_VANILMIRTH: i32 = 6004;
pub const MER_LIF2: i32 = 6005;
pub const MER_AMISTR2: i32 = 6006;
pub const MER_FILIR2: i32 = 6007;
pub const MER_VANILMIRTH2: i32 = 6008;
pub const MER_LIF_H: i32 = 6009;
pub const MER_AMISTR_H: i32 = 6010;
pub const MER_FILIR_H: i32 = 6011;
pub const MER_VANILMIRTH_H: i32 = 6012;
pub const MER_LIF_H2: i32 = 6013;
pub const MER_AMISTR_H2: i32 = 6014;
pub const MER_FILIR_H2: i32 = 6015;
pub const MER_VANILMIRTH_H2: i32 = 6016;
pub const MER_EIRA: i32 = 6048;
pub const MER_BAYERI: i32 = 6049;
pub const MER_SERA: i32 = 6050;
pub const MER_DIETER: i32 = 6051;
pub const MER_ELEANOR: i32 = 6052;

/// homunculus.hpp enum homun_type.
pub const HT_REG: i32 = 0x1;
pub const HT_EVO: i32 = 0x2;
pub const HT_S: i32 = 0x4;
pub const HT_INVALID: i32 = -1;

/// homunculus.hpp enum homun_setting (battle_config.hom_setting).
pub const HOMSET_NO_SUPPORT_SKILL: i32 = 0x01;
pub const HOMSET_NO_INSTANT_LAND_SKILL: i32 = 0x02;
pub const HOMSET_FIRST_TARGET: i32 = 0x04;
pub const HOMSET_COPY_SPEED: i32 = 0x08;
pub const HOMSET_DISPLAY_LUK: i32 = 0x10;
pub const HOMSET_SAME_MATK: i32 = 0x20;

/// homunculus.hpp enum e_homun_grade.
pub const HOMGRADE_HATE_WITH_PASSION: u8 = 0;
pub const HOMGRADE_HATE: u8 = 1;
pub const HOMGRADE_AWKWARD: u8 = 2;
pub const HOMGRADE_SHY: u8 = 3;
pub const HOMGRADE_NEUTRAL: u8 = 4;
pub const HOMGRADE_CORDIAL: u8 = 5;
pub const HOMGRADE_LOYAL: u8 = 6;

/// One entry of hd->scd (std::unordered_map<uint16, int32>: skill id → the skill_blockhomun_end timer).
#[derive(spacetimedb::SpacetimeType, Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct HomScdTimer {
    pub skill_id: u16,
    pub timer: u64,
}

/// homunculus.hpp struct homun_data less its block_list / unit_data / view_data / status_change (the `unit` row and
/// the status-change rows). Timer fields hold timer ids (0 = INVALID_TIMER).
#[table(accessor = hom_data)]
#[derive(Clone, Debug)]
pub struct HomData {
    /// block_list id (npc_get_new_npc_id).
    #[primary_key]
    pub id: u32,
    /// hd->master: the owner's account id (sd->hd is the row whose master is sd).
    #[unique]
    pub master: u32,
    /// hd->homunculus (mmo.hpp s_homunculus). hd->homunculusDB is homunculus_db.homun_search(homunculus.class_) as
    /// hom_alloc / hom_change_class set it: [`HomData::db_class`] keeps the database entry's key.
    pub homunculus: SHomunculus,
    /// hd->homunculusDB: the base class of the database entry (homun_search's result).
    pub db_class: i32,
    pub base_status: StatusData,
    pub battle_status: StatusData,
    /// hd->regen (its tick part: regen_tick_hp / regen_tick_sp).
    pub regen: RegenData,
    pub regen_tick_hp: Tick,
    pub regen_tick_sp: Tick,
    pub masterteleport_timer: u64,
    pub hungry_timer: u64,
    pub exp_next: u64,
    /// hd->scd: the entries (skill → timer) and the unordered_map's iteration order state (skill/umap.rs).
    pub scd: Vec<HomScdTimer>,
    pub scd_order: Vec<u16>,
    pub scd_buckets: u64,
    pub scd_next_resize: u64,
}

impl HomData {
    /// hd->scd's order state.
    pub fn scd_map(&self) -> crate::skill::umap::Umap {
        crate::skill::umap::Umap { order: self.scd_order.clone(), buckets: self.scd_buckets, next_resize: self.scd_next_resize }
    }
    pub fn set_scd_map(&mut self, m: crate::skill::umap::Umap) {
        self.scd_order = m.order;
        self.scd_buckets = m.buckets;
        self.scd_next_resize = m.next_resize;
    }
    /// hd->scd in the unordered_map's iteration order.
    pub fn scd_entries(&self) -> Vec<HomScdTimer> {
        self.scd_order.iter().filter_map(|k| self.scd.iter().find(|e| e.skill_id == *k).copied()).collect()
    }
    /// hd->homunculusDB.
    pub fn db(&self) -> Option<&'static crate::db::misc::homunculus::HomunculusDb> {
        crate::db::misc::homunculus::homunculus_db().find(self.db_class)
    }
}

// ---------------------------------------------------------------------------------------------- access

/// A homunculus by its block id (map_id2hd).
pub fn get(c: &Ctx, id: u32) -> Option<HomData> {
    c.db().hom_data().id().find(id)
}

/// sd->hd: the homunculus of the player with account id `aid` (loaded, active or not; not one unit_free has already
/// detached from its master).
pub fn of_master(c: &Ctx, aid: u32) -> Option<HomData> {
    c.db().hom_data().master().find(aid).filter(|hd| !is_detached(hd.id))
}

thread_local! {
    /// The homunculi unit_free has cut from their master (`sd->hd = nullptr; hd->master = nullptr;`, unit.cpp:4155-4157)
    /// while their row still exists (its status changes end after that: no master, so no client update).
    static DETACHED: std::cell::RefCell<Vec<u32>> = const { std::cell::RefCell::new(Vec::new()) };
}

/// unit_free's `sd->hd = nullptr; hd->master = nullptr;`.
pub fn detach(id: u32) {
    DETACHED.with(|d| {
        let mut d = d.borrow_mut();
        if !d.contains(&id) {
            d.push(id);
        }
    });
}

/// The row is gone (map_deliddb): forget the detached mark.
pub fn forget_detached(id: u32) {
    DETACHED.with(|d| d.borrow_mut().retain(|&x| x != id));
}

pub fn is_detached(id: u32) -> bool {
    DETACHED.with(|d| d.borrow().contains(&id))
}

/// hd->master != nullptr: the master's account id when the master is in the world and the link is not cut.
pub fn master_sd(c: &Ctx, hd: &HomData) -> Option<u32> {
    if is_detached(hd.id) || crate::pc::get(c, hd.master).is_none() {
        return None;
    }
    Some(hd.master)
}

/// Saves the row and keeps the unit row's copies of what clif, the walking code and the area searches read (name,
/// speed, mode, level, HP, size).
pub fn put(c: &Ctx, hd: &HomData) {
    if c.db().hom_data().id().find(hd.id).is_some() {
        c.db().hom_data().id().update(hd.clone());
    } else {
        c.db().hom_data().insert(hd.clone());
    }
    sync_unit(c, hd);
}

/// The unit row mirrors hd->homunculus.name (clif_name, the unit packets), battle_status.speed / mode / hp / size and
/// homunculus.level (status_get_lv).
pub fn sync_unit(c: &Ctx, hd: &HomData) {
    let Some(mut u) = unit::get(c, hd.id) else { return };
    let st = &hd.battle_status;
    let (hp, max_hp) = (st.hp as i64, st.max_hp as i64);
    let lv = hd.homunculus.level as i32;
    if u.name != hd.homunculus.name || u.speed != st.speed || u.mode != st.mode as u32 || u.level != lv || u.hp != hp || u.max_hp != max_hp || u.size != st.size || u.dead != (st.hp == 0) {
        u.name = hd.homunculus.name.clone();
        u.speed = st.speed;
        u.mode = st.mode as u32;
        u.level = lv;
        u.hp = hp;
        u.max_hp = max_hp;
        u.size = st.size;
        u.dead = st.hp == 0;
        unit::save(c, &u);
    }
}

/// The homunculus' unit row (block_list / unit_data / view_data).
pub fn unit_of(c: &Ctx, hd: &HomData) -> Option<Unit> {
    unit::get(c, hd.id)
}

/// Runs `f` on the row of homunculus `id` and saves it (None when there is no such homunculus).
pub fn with<R>(c: &Ctx, id: u32, f: impl FnOnce(&mut HomData) -> R) -> Option<R> {
    let mut hd = get(c, id)?;
    let r = f(&mut hd);
    put(c, &hd);
    Some(r)
}

/// homunculus.hpp hom_is_active: the homunculus exists, is not vaporized / morphed and is alive.
pub fn hom_is_active(hd: Option<&HomData>) -> bool {
    hd.map(|h| h.homunculus.vaporize as i32 == HOM_ST_ACTIVE && h.battle_status.hp > 0).unwrap_or(false)
}

/// `hom_is_active(sd->hd)` for the player `aid`.
pub fn is_active_of(c: &Ctx, aid: u32) -> bool {
    hom_is_active(of_master(c, aid).as_ref())
}
