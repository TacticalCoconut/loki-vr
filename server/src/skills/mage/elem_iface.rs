//! The elemental calls of the Sorcerer classes (SO_SUMMON_AGNI / AQUA / VENTUS / TERA, SO_EL_ACTION,
//! SO_ELEMENTAL_SHIELD, SO_EL_CONTROL, SO_EL_CURE; the Elemental Master's EM_ELEMENTAL_BUSTER / VEIL /
//! SUMMON_ELEMENTAL_*), in one place until the **mercelem** agent publishes its interface (elemental.cpp:
//! elemental_create, elemental_delete, elemental_action, elemental_change_mode, and map_session_data::ed).
//!
//! Integration branch mage-elem: each function now calls the mercelem agent's port (crate::elemental, COORD 08:40).
//! On branch mage they are stubs answering what rAthena's code sees with no elemental (`sd->ed == nullptr`).
//! The class files keep rAthena's branch structure around these calls; replacing the bodies below with the mercelem
//! agent's functions is the whole integration.

use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// pc.hpp `map_session_data::ed` (`s_elemental_data *ed`): the block id of the player's summoned elemental,
/// None = nullptr. (mercelem agent: elemental.cpp / sd->ed)
///
/// Stub: no elemental system in this build yet, so every player has `sd->ed == nullptr`.
pub fn sd_ed(c: &Ctx, aid: u32) -> Option<u32> {
    crate::elemental::data::of_master(c, aid).map(|ed| ed.id)
}

/// `sd->ed->elemental.class_` (the elemental's own record; the same class as `sd->ed->db->class_`, an
/// ELEMENTALID_*): the class of the elemental
/// `ed_id`. (mercelem agent: elemental.cpp s_elemental_data::db)
///
/// Stub: only reached with an elemental (sd_ed returned Some), which never happens yet; answers 0 (no class).
pub fn ed_class(c: &Ctx, ed_id: u32) -> i32 {
    crate::elemental::data::get(c, ed_id).map(|ed| ed.elemental.class_ as i32).unwrap_or(0)
}

/// elemental.cpp `int32 elemental_create(map_session_data *sd, int32 class_, uint32 lifetime)`: summons elemental
/// `class_` for `lifetime` ms; 1 on success, 0 when it could not be created (unknown class).
/// (mercelem agent: elemental.cpp elemental_create)
///
/// Stub: no elemental can be created in this build, so it answers 0 (the caller's failure branch, as rAthena's
/// "Unknown elemental class" path). rAthena answers 1 for a valid class: the summons' parity waits for mercelem.
pub fn elemental_create(c: &Ctx, sd: &Unit, class_: i32, lifetime: u32) -> i32 {
    crate::elemental::elemental_create(c, sd.id, class_, lifetime)
}

/// elemental.cpp `int32 elemental_delete(s_elemental_data *ed)`: removes the elemental (`nullpo_ret(ed)`: 0 for
/// nullptr). (mercelem agent: elemental.cpp elemental_delete)
///
/// Stub: only reached with an elemental (sd_ed returned Some), which never happens yet; answers 0.
pub fn elemental_delete(c: &Ctx, ed_id: u32) -> i32 {
    crate::elemental::elemental_delete(c, ed_id)
}

/// elemental.cpp `int32 elemental_action(s_elemental_data *ed, block_list *bl, t_tick tick)`: the elemental uses its
/// action skill on `bl` (`nullpo_ret(ed)`: 0 for nullptr). (mercelem agent: elemental.cpp elemental_action)
///
/// Stub: only reached with an elemental (sd_ed returned Some), which never happens yet; answers 0.
pub fn elemental_action(c: &Ctx, ed_id: u32, bl: &Unit, tick: Tick) -> i32 {
    crate::elemental::elemental_action(c, ed_id, bl, tick)
}

/// elemental.cpp `int32 elemental_change_mode(s_elemental_data *ed, int32 mode)`: sets the elemental's AI mode
/// (EL_MODE_*) and its mode skill; `nullpo_ret(ed)`: 0 for nullptr. (mercelem agent: elemental.cpp
/// elemental_change_mode)
///
/// Stub: only reached with an elemental (sd_ed returned Some), which never happens yet; answers 0.
pub fn elemental_change_mode(c: &Ctx, ed_id: u32, mode: i32) -> i32 {
    crate::elemental::elemental_change_mode(c, ed_id, mode)
}
