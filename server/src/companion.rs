//! The parts of rAthena's generic unit / status code that mercenaries (BL_MER, mercenary.cpp) and elementals (BL_ELEM,
//! elemental.cpp) share: their status data (status_get_status_data / status_get_base_status / status_get_regen_data
//! for the two types), the status calculation (status_calc_mercenary_ / status_calc_elemental_, status_calc_misc,
//! status_calc_regen / _rate, status_calc_bl_main and status_calc_bl_'s client updates: `status.rs`), HP / SP
//! (status_damage / status_heal / natural heal for the two types: `hp.rs`) and the unit.cpp arms (unit_remove_map,
//! unit_free, unit_warp, the master teleport timer, unit_remove_map_pc / unit_free_pc: `unit.rs`), and the inter-link
//! messages of both (intif.cpp ↔ char/int_mercenary.cpp, char/int_elemental.cpp).
//!
//! Homunculi (BL_HOM) have the same arms in rAthena; they are the homun agent's.

pub mod buildins;
pub mod hp;
pub mod status;
pub mod unit;

use crate::elemental::data::ElementalData;
use crate::int_elemental::SElemental;
use crate::int_mercenary::SMercenary;
use crate::map::{BL_ELEM, BL_MER};
use crate::mercenary::data::MercenaryData;
use crate::status::data::{RegenData, StatusData};
use crate::world::{Ctx, Tick};

// ---------------------------------------------------------------------------------------------- inter link

/// The mercenary / elemental messages on the inter link (intif.cpp ↔ char/int_mercenary.cpp, char/int_elemental.cpp).
#[derive(Clone, Debug)]
pub enum MercElemInter {
    // ---- map → char
    /// 0x3070 intif_mercenary_create.
    MercenaryCreate { merc: SMercenary },
    /// 0x3071 intif_mercenary_request.
    MercenaryRequest { merc_id: i32, char_id: u32 },
    /// 0x3072 intif_mercenary_delete.
    MercenaryDelete { merc_id: i32 },
    /// 0x3073 intif_mercenary_save.
    MercenarySave { merc: SMercenary },
    /// 0x307c intif_elemental_create.
    ElementalCreate { ele: SElemental },
    /// 0x307d intif_elemental_request.
    ElementalRequest { ele_id: i32, char_id: u32 },
    /// 0x307e intif_elemental_delete.
    ElementalDelete { ele_id: i32 },
    /// 0x307f intif_elemental_save.
    ElementalSave { ele: SElemental },
    // ---- char → map
    /// 0x3870 mapif_mercenary_send (intif_parse_mercenary_received).
    MercenaryReceived { merc: SMercenary, flag: bool },
    /// 0x3871 mapif_mercenary_deleted (intif_parse_mercenary_deleted).
    MercenaryDeleted { flag: bool },
    /// 0x3872 mapif_mercenary_saved (intif_parse_mercenary_saved).
    MercenarySaved { flag: bool },
    /// 0x387c mapif_elemental_send (intif_parse_elemental_received).
    ElementalReceived { ele: SElemental, flag: bool },
    /// 0x387d mapif_elemental_deleted (intif_parse_elemental_deleted).
    ElementalDeleted { flag: bool },
    /// 0x387e mapif_elemental_saved (intif_parse_elemental_saved).
    ElementalSaved { flag: bool },
}

/// char/inter.cpp inter_mercenary_parse_frommap / inter_elemental_parse_frommap, and map/intif.cpp intif_parse's
/// mercenary / elemental arms.
pub fn intif_dispatch(c: &Ctx, m: MercElemInter) {
    match m {
        MercElemInter::MercenaryCreate { merc } => crate::int_mercenary::mapif_parse_mercenary_create(c, merc),
        MercElemInter::MercenaryRequest { merc_id, char_id } => crate::int_mercenary::mapif_parse_mercenary_load(c, merc_id, char_id),
        MercElemInter::MercenaryDelete { merc_id } => crate::int_mercenary::mapif_parse_mercenary_delete(c, merc_id),
        MercElemInter::MercenarySave { merc } => crate::int_mercenary::mapif_parse_mercenary_save(c, merc),
        MercElemInter::ElementalCreate { ele } => crate::int_elemental::mapif_parse_elemental_create(c, ele),
        MercElemInter::ElementalRequest { ele_id, char_id } => crate::int_elemental::mapif_parse_elemental_load(c, ele_id, char_id),
        MercElemInter::ElementalDelete { ele_id } => crate::int_elemental::mapif_parse_elemental_delete(c, ele_id),
        MercElemInter::ElementalSave { ele } => crate::int_elemental::mapif_parse_elemental_save(c, ele),
        // intif_parse_mercenary_received: (the size check cannot fail here)
        MercElemInter::MercenaryReceived { merc, flag } => {
            crate::mercenary::mercenary_recv_data(c, merc, flag);
        }
        // intif_parse_mercenary_deleted
        MercElemInter::MercenaryDeleted { flag } => {
            if !flag {
                log::error!("Mercenary data delete failure");
            }
        }
        // intif_parse_mercenary_saved
        MercElemInter::MercenarySaved { flag } => {
            if !flag {
                log::error!("Mercenary data save failure");
            }
        }
        // intif_parse_elemental_received
        MercElemInter::ElementalReceived { ele, flag } => {
            crate::elemental::elemental_data_received(c, ele, flag);
        }
        // intif_parse_elemental_deleted
        MercElemInter::ElementalDeleted { flag } => {
            if !flag {
                log::error!("Elemental data delete failure");
            }
        }
        // intif_parse_elemental_saved
        MercElemInter::ElementalSaved { flag } => {
            if !flag {
                log::error!("Elemental data save failure");
            }
        }
    }
}

// ---------------------------------------------------------------------------------------------- the two row types

/// A mercenary's or an elemental's data (the block_list is its `unit` row).
#[derive(Clone, Debug)]
pub enum Comp {
    Mer(MercenaryData),
    Elem(ElementalData),
}

impl Comp {
    pub fn id(&self) -> u32 {
        match self {
            Comp::Mer(m) => m.id,
            Comp::Elem(e) => e.id,
        }
    }
    /// block_list::type.
    pub fn kind(&self) -> u16 {
        match self {
            Comp::Mer(_) => BL_MER,
            Comp::Elem(_) => BL_ELEM,
        }
    }
    /// ->master (account id).
    pub fn master(&self) -> u32 {
        match self {
            Comp::Mer(m) => m.master,
            Comp::Elem(e) => e.master,
        }
    }
    pub fn battle_status(&self) -> &StatusData {
        match self {
            Comp::Mer(m) => &m.battle_status,
            Comp::Elem(e) => &e.battle_status,
        }
    }
    pub fn battle_status_mut(&mut self) -> &mut StatusData {
        match self {
            Comp::Mer(m) => &mut m.battle_status,
            Comp::Elem(e) => &mut e.battle_status,
        }
    }
    pub fn base_status(&self) -> &StatusData {
        match self {
            Comp::Mer(m) => &m.base_status,
            Comp::Elem(e) => &e.base_status,
        }
    }
    pub fn base_status_mut(&mut self) -> &mut StatusData {
        match self {
            Comp::Mer(m) => &mut m.base_status,
            Comp::Elem(e) => &mut e.base_status,
        }
    }
    pub fn regen(&self) -> &RegenData {
        match self {
            Comp::Mer(m) => &m.regen,
            Comp::Elem(e) => &e.regen,
        }
    }
    pub fn regen_mut(&mut self) -> &mut RegenData {
        match self {
            Comp::Mer(m) => &mut m.regen,
            Comp::Elem(e) => &mut e.regen,
        }
    }
    /// regen.tick.hp / regen.tick.sp.
    pub fn regen_ticks(&self) -> (Tick, Tick) {
        match self {
            Comp::Mer(m) => (m.regen_tick_hp, m.regen_tick_sp),
            Comp::Elem(e) => (e.regen_tick_hp, e.regen_tick_sp),
        }
    }
    pub fn set_regen_ticks(&mut self, hp: Tick, sp: Tick) {
        match self {
            Comp::Mer(m) => {
                m.regen_tick_hp = hp;
                m.regen_tick_sp = sp;
            }
            Comp::Elem(e) => {
                e.regen_tick_hp = hp;
                e.regen_tick_sp = sp;
            }
        }
    }
    pub fn masterteleport_timer(&self) -> u64 {
        match self {
            Comp::Mer(m) => m.masterteleport_timer,
            Comp::Elem(e) => e.masterteleport_timer,
        }
    }
    pub fn set_masterteleport_timer(&mut self, t: u64) {
        match self {
            Comp::Mer(m) => m.masterteleport_timer = t,
            Comp::Elem(e) => e.masterteleport_timer = t,
        }
    }
    /// status.cpp status_get_lv: `db->lv` for both types.
    pub fn level(&self) -> i32 {
        match self {
            Comp::Mer(m) => crate::mercenary::data::db_of(m).map(|d| d.lv as i32).unwrap_or(1),
            Comp::Elem(e) => crate::elemental::data::db_of(e).map(|d| d.lv as i32).unwrap_or(1),
        }
    }
    /// status.cpp status_get_class: mercenary.class_ / elemental.class_.
    pub fn class(&self) -> i32 {
        match self {
            Comp::Mer(m) => m.mercenary.class_ as i32,
            Comp::Elem(e) => e.elemental.class_ as i32,
        }
    }
}

/// map_id2bl for a mercenary or an elemental.
pub fn get(c: &Ctx, id: u32) -> Option<Comp> {
    if let Some(m) = crate::mercenary::data::get(c, id) {
        return Some(Comp::Mer(m));
    }
    crate::elemental::data::get(c, id).map(Comp::Elem)
}

/// Stores the row (and the unit row's status copies).
pub fn put(c: &Ctx, x: &Comp) {
    match x {
        Comp::Mer(m) => crate::mercenary::data::put(c, m),
        Comp::Elem(e) => crate::elemental::data::put(c, e),
    }
}

/// Whether `kind` is one of the two types handled here.
pub fn is_kind(kind: u16) -> bool {
    kind == BL_MER || kind == BL_ELEM
}

/// The unit row mirrors what clif and the walking code read of the companion (status_get_speed, the mode, HP for the
/// HP bars, status_get_lv, the name clif_name sends, the size).
pub fn sync_unit(c: &Ctx, id: u32, st: &StatusData, level: i32, name: &str) {
    let Some(mut u) = crate::unit::get(c, id) else { return };
    let (hp, max_hp) = (st.hp as i64, st.max_hp as i64);
    if u.speed != st.speed || u.mode != st.mode as u32 || u.hp != hp || u.max_hp != max_hp || u.level != level || u.name != name || u.size != st.size || u.dead != (st.hp == 0) {
        u.speed = st.speed;
        u.mode = st.mode as u32;
        u.hp = hp;
        u.max_hp = max_hp;
        u.level = level;
        u.name = name.to_string();
        u.size = st.size;
        u.dead = st.hp == 0;
        crate::unit::save(c, &u);
    }
}

/// status_get_status_data for BL_MER / BL_ELEM (the battle status).
pub fn status_of(c: &Ctx, id: u32) -> Option<StatusData> {
    get(c, id).map(|x| x.battle_status().clone())
}

/// status_get_base_status for BL_MER / BL_ELEM.
pub fn base_status_of(c: &Ctx, id: u32) -> Option<StatusData> {
    get(c, id).map(|x| x.base_status().clone())
}

/// unit.cpp unit_get_master / battle_get_master's BL_MER / BL_ELEM arm: the master's account id.
pub fn master_of(c: &Ctx, id: u32) -> Option<u32> {
    get(c, id).map(|x| x.master())
}

/// The ids in regen_db of every mercenary and elemental (map_addiddb puts BL_REGEN types there).
pub fn regen_ids(c: &Ctx) -> Vec<u32> {
    use crate::elemental::data::elemental_data;
    use crate::mercenary::data::mercenary_data;
    use spacetimedb::Table;
    let mut v: Vec<u32> = c.db().mercenary_data().iter().map(|m| m.id).collect();
    v.extend(c.db().elemental_data().iter().map(|e| e.id));
    v
}

/// clif.cpp clif_refresh's companion part: `if( sd->md ) { clif_mercenary_info(sd); clif_mercenary_skillblock(sd); }
/// if( sd->ed ) clif_elemental_info(sd);` (after the homunculus' clif_send_homdata: homun agent).
pub fn clif_refresh(c: &Ctx, aid: u32) {
    if crate::mercenary::data::of_master(c, aid).is_some() {
        crate::mercenary::clif::clif_mercenary_info(c, aid);
        crate::mercenary::clif::clif_mercenary_skillblock(c, aid);
    }
    if crate::elemental::data::of_master(c, aid).is_some() {
        crate::elemental::clif::clif_elemental_info(c, aid);
    }
}
