//! What battle needs from the mob agent's system (mob.cpp, the monster side of status.cpp and unit.cpp's chase code).
//! Wired by the mob agent (branch `mob`, server/ARCHITECTURE.md): every function reads the monster's `mob_data` row (src/mob.rs)
//! or calls the mob port. The mob agent owns status_check_skilluse / status_check_visibility (server/ARCHITECTURE.md): the
//! normal-attack subset that stood here is replaced by the full port (`crate::status::check::…`).

use crate::map::{BL_ELEM, BL_HOM, BL_MER, BL_MOB, BL_NPC, BL_PC};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// map.hpp enum mob_ai.
pub const AI_NONE: i32 = 0;
pub const AI_ATTACK: i32 = 1;
pub const AI_SPHERE: i32 = 2;
pub const AI_FLORA: i32 = 3;
pub const AI_ZANZOU: i32 = 4;
pub const AI_LEGION: i32 = 5;
pub const AI_FAW: i32 = 6;
pub const AI_GUILD: i32 = 7;
pub const AI_WAVEMODE: i32 = 8;
pub const AI_ABR: i32 = 9;
pub const AI_BIONIC: i32 = 10;

/// mob.hpp MobSkillState.
pub const MSS_IDLE: i32 = 0;
pub const MSS_BERSERK: i32 = 4;
pub const MSS_RUSH: i32 = 6;

/// mob.hpp MOBID_EMPERIUM.
pub const MOBID_EMPERIUM: u32 = 1288;

/// common/mmo.hpp e_mode MD_LOOTER / MD_ASSIST.
pub const MD_LOOTER: i32 = 0x0000002;
pub const MD_ASSIST: i32 = 0x0000008;

/// md->mob_id. 0: the unit has no monster row.
pub fn mob_id(c: &Ctx, md: &Unit) -> u32 {
    crate::mob::get(c, md.id).map(|m| m.mob_id as u32).unwrap_or(0)
}

/// md->master_id: slaves and summons.
pub fn master_id(c: &Ctx, md: &Unit) -> u32 {
    crate::mob::get(c, md.id).map(|m| m.master_id).unwrap_or(0)
}

/// md->special_state.ai.
pub fn special_ai(c: &Ctx, md: &Unit) -> i32 {
    crate::mob::get(c, md.id).map(|m| m.special_ai as i32).unwrap_or(AI_NONE)
}

/// md->guardian_data: Some(guardian_data->guild_id) when the monster has guardian data (the castle guardians and the
/// Emperium: woe/guardian.rs).
pub fn guardian_guild_id(c: &Ctx, md: &Unit) -> Option<i32> {
    crate::woe::guardian::get(c, md.id).map(|gd| gd.guild_id)
}

/// md->db->race2.
pub fn race2(c: &Ctx, md: &Unit) -> Vec<u8> {
    crate::mob::get(c, md.id).and_then(|m| crate::mob::db(c, &m).map(|d| d.race2.clone())).unwrap_or_default()
}

/// md->damagetaken (100 = unchanged, the Green Aura MVPs take less).
pub fn damagetaken(c: &Ctx, md: &Unit) -> i64 {
    crate::mob::get(c, md.id).map(|m| m.damagetaken as i64).unwrap_or(100)
}

/// md->state.skillstate.
pub fn skillstate(c: &Ctx, md: &Unit) -> i32 {
    crate::mob::get(c, md.id).map(|m| m.skillstate).unwrap_or(MSS_IDLE)
}

/// md->target_id.
pub fn target_id(c: &Ctx, md: &Unit) -> u32 {
    crate::mob::get(c, md.id).map(|m| m.target_id).unwrap_or(0)
}

/// mob.cpp mob_randomtarget: may replace the target; false = keep walking, no attack.
pub fn randomtarget(c: &Ctx, md: &Unit, target_id: &mut u32) -> bool {
    crate::mob::ai::mob_randomtarget(c, md.id, target_id)
}

/// mob.cpp mob_ai_sub_hard_attacktimer: the monster AI runs the timed attack (1: attackable, 0: not).
pub fn ai_sub_hard_attacktimer(c: &Ctx, md: &Unit, tick: Tick) -> i32 {
    crate::mob::ai::mob_ai_sub_hard_attacktimer(c, md.id, tick) as i32
}

/// mob.cpp mob_setstate (saves the monster row).
pub fn setstate(c: &Ctx, md: &Unit, state: i32) {
    if let Some(mut m) = crate::mob::get(c, md.id) {
        crate::mob::mob_setstate(&mut m, state);
        crate::mob::put(c, &m);
    }
}

/// mob.cpp mob_set_delay: the monster's inactive-AI time and skill delays after an attack.
pub fn set_delay(c: &Ctx, md: &Unit, tick: Tick, event: i32) {
    crate::mob::ai::mob_set_delay(c, md.id, tick, event);
}

/// mob.cpp mob_unlocktarget.
pub fn unlocktarget(c: &Ctx, md: &Unit, tick: Tick) {
    crate::mob::ai::mob_unlocktarget(c, md.id, tick);
}

/// unit.cpp unit_attack_timer_sub's link: `if (status_has_mode(sstatus,MD_ASSIST) && DIFF_TICK(tick,
/// md->last_linktime) >= MIN_MOBLINKTIME) { md->last_linktime = tick; map_foreachinshootrange(mob_linksearch, src,
/// battle_config.assist_range, BL_MOB, md->mob_id, target->id, tick); }` (the caller has tested MD_ASSIST).
pub fn link_assist(c: &Ctx, md: &Unit, target_id: u32, tick: Tick) {
    let Some(mut m) = crate::mob::get(c, md.id) else { return };
    if tick - m.last_linktime < crate::mob::MIN_MOBLINKTIME {
        return;
    }
    // Link monsters nearby [Skotlex]
    m.last_linktime = tick;
    crate::mob::put(c, &m);
    crate::mob::ai::mob_linksearch_area(c, md, m.mob_id, target_id, tick);
}

/// unit.cpp unit_walktobl (mob agent's port, src/mob/unit.rs): 1 = the walk started.
pub fn unit_walktobl(c: &Ctx, bl: &Unit, tbl: &Unit, range: i32, flag: u8) -> i32 {
    crate::mob::unit::unit_walktobl(c, bl.id, tbl.id, range, flag as i32) as i32
}

/// status_get_sc(bl) != nullptr: the unit kinds that carry a status_change.
pub fn has_sc(kind: u16) -> bool {
    matches!(kind, BL_PC | BL_MOB | BL_NPC | BL_HOM | BL_MER | BL_ELEM)
}

/// status.hpp status_has_mode.
pub fn has_mode(mode: i32, md: i32) -> bool {
    mode & md == md
}

/// status.cpp status_check_skilluse (the mob agent's full port).
pub fn status_check_skilluse(c: &Ctx, src: Option<&Unit>, target: Option<&Unit>, skill_id: u16, flag: i32) -> bool {
    crate::status::check::status_check_skilluse(c, src, target, skill_id, flag)
}

/// status.cpp status_check_visibility (the mob agent's full port: monsters see as far as their chase range).
pub fn status_check_visibility(c: &Ctx, src: &Unit, target: &Unit, checkblind: bool) -> bool {
    crate::status::check::status_check_visibility(c, src, target, checkblind)
}
