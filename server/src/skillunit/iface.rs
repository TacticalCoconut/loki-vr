//! What the skill unit code calls in other agents' systems, at one place. Each function has the rAthena signature the
//! unit code needs and forwards to the owner's port (one line each); where the owner's port is not in this branch yet,
//! the body is a commented stub naming the owner that does nothing (and says so in the log) rather than faking an
//! effect.
//!
//! - skillcore (merged d22a995): crate::skill::{attack, effect, misc, castend, area, timerskill, condition, clif, state},
//!   crate::skills (the SkillImpl registry), crate::mob::skill::mobskill_event.
//! - status2 (merged in its branch): status_change_clear_buffs.
//! - bg (not merged here): bg_team_get_id.

use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

#[allow(dead_code)] // (for the stubs whose owner's branch is not merged here)
fn missing(what: &str, owner: &str) {
    log::warn!("skillunit: {what} is not in this build yet ({owner} agent)");
}

// ---------------------------------------------------------------------------------------------- skillcore

/// skill.cpp skill_attack(attack_type, src, dsrc, bl, skill_id, skill_lv, tick, flag) (skillcore: skill/attack.rs).
#[allow(clippy::too_many_arguments)]
pub fn skill_attack(c: &Ctx, attack_type: i32, src: &Unit, dsrc: &Unit, bl: &Unit, skill_id: u16, skill_lv: u16, tick: Tick, flag: i32) -> i64 {
    crate::skill::attack::skill_attack(c, attack_type, src, dsrc, bl, skill_id, skill_lv, tick, flag)
}

/// skill.cpp skill_additional_effect(src, bl, skill_id, skill_lv, attack_type, dmg_lv, tick) (skillcore: skill/effect.rs).
#[allow(clippy::too_many_arguments)]
pub fn skill_additional_effect(c: &Ctx, src: &Unit, bl: &Unit, skill_id: u16, skill_lv: u16, attack_type: i32, dmg_lv: u8, tick: Tick) -> i32 {
    crate::skill::effect::skill_additional_effect(c, src, bl, skill_id, skill_lv, attack_type, dmg_lv, tick)
}

/// skill.cpp skill_blown(src, target, count, dir, flag) (skillcore: skill/attack.rs). `dir` -1: away from src.
pub fn skill_blown(c: &Ctx, src: &Unit, target: &Unit, count: i32, dir: i8, flag: i32) -> i32 {
    crate::skill::attack::skill_blown(c, src, target, count, dir, flag)
}

/// skill.cpp skill_calc_heal(src, target, skill_id, skill_lv, heal) (skillcore: skill/misc.rs).
pub fn skill_calc_heal(c: &Ctx, src: &Unit, target: &Unit, skill_id: u16, skill_lv: u16, heal: bool) -> i32 {
    crate::skill::misc::skill_calc_heal(c, src, Some(target), skill_id, skill_lv, heal)
}

/// skill.cpp skill_castend_damage_id(src, bl, skill_id, skill_lv, tick, flag) (skillcore: skill/castend.rs).
pub fn skill_castend_damage_id(c: &Ctx, src: &Unit, bl: &Unit, skill_id: u16, skill_lv: u16, tick: Tick, flag: i32) -> i32 {
    crate::skill::castend::skill_castend_damage_id(c, src, bl, skill_id, skill_lv, tick, flag)
}

/// `map_foreachinrange(skill_area_sub, center, range, type, src, skill_id, skill_lv, tick, flag,
/// skill_castend_damage_id)` (skillcore: skill/area.rs; map_foreachinrange = the configured wall check).
#[allow(clippy::too_many_arguments)]
pub fn skill_area_sub_castend_damage_id(c: &Ctx, center: &Unit, range: i16, kinds: u16, src: &Unit, skill_id: u16, skill_lv: u16, tick: Tick, flag: i32) -> i32 {
    use crate::skill::area;
    area::foreach_in_range(c, center, range, kinds, area::Wall::Config, |c, bl| {
        area::skill_area_sub(c, bl, src, skill_id, skill_lv, tick, flag, crate::skill::castend::skill_castend_damage_id)
    })
}

/// skill.cpp skill_addtimerskill(src, tick, target, x, y, skill_id, skill_lv, type, flag) (skillcore: skill/timerskill.rs).
#[allow(clippy::too_many_arguments)]
pub fn skill_addtimerskill(c: &Ctx, src: &Unit, tick: Tick, target: u32, x: i16, y: i16, skill_id: u16, skill_lv: u16, kind: i32, flag: i32) -> i32 {
    crate::skill::timerskill::skill_addtimerskill(c, src, tick, target, x, y, skill_id, skill_lv, kind, flag)
}

/// skill.cpp skill_get_requirement(sd, skill_id, skill_lv).itemid (skillcore: skill/condition.rs): the item ids of the
/// requirement's MAX_SKILL_ITEM_REQUIRE slots. None without a player (`if( !sd ) return req;`: no item).
pub fn skill_get_requirement_items(c: &Ctx, sd: Option<&Unit>, skill_id: u16, skill_lv: u16) -> Option<[u32; crate::db::skills::MAX_SKILL_ITEM_REQUIRE]> {
    sd.map(|sd| crate::skill::condition::skill_get_requirement(c, sd, skill_id, skill_lv).itemid)
}

/// skill.cpp skill_check_condition_castend(sd, skill_id, skill_lv) (skillcore: skill/condition.rs). A player who is
/// gone fails, as a nullptr sd does.
pub fn skill_check_condition_castend(c: &Ctx, aid: u32, skill_id: u16, skill_lv: u16) -> bool {
    match unit::get(c, aid) {
        Some(sd) => crate::skill::condition::skill_check_condition_castend(c, &sd, skill_id, skill_lv),
        None => false,
    }
}

/// skill.cpp skill_consume_requirement(sd, skill_id, skill_lv, type) (skillcore: skill/condition.rs).
pub fn skill_consume_requirement(c: &Ctx, aid: u32, skill_id: u16, skill_lv: u16, kind: i16) {
    if let Some(sd) = unit::get(c, aid) {
        crate::skill::condition::skill_consume_requirement(c, &sd, skill_id, skill_lv, kind);
    }
}

/// skill.cpp skill_onskillusage(sd, bl, skill_id, tick) (skillcore: skill/effect.rs, autospell on skill use).
pub fn skill_onskillusage(c: &Ctx, aid: u32, bl: Option<&Unit>, skill_id: u16, tick: Tick) {
    crate::skill::effect::skill_onskillusage(c, aid, bl, skill_id, tick);
}

/// `skill->impl->castendPos2(src, x, y, skill_lv, tick, flag)` of skill_castend_pos2 (skillcore: the SkillImpl registry,
/// crate::skills::skill_impl). None when the skill has no implementation (`skill->impl == nullptr`: "Unknown skill
/// used").
#[allow(clippy::too_many_arguments)]
pub fn impl_castend_pos2(c: &Ctx, src: &Unit, x: i16, y: i16, skill_id: u16, skill_lv: u16, tick: Tick, flag: &mut i32) -> Option<()> {
    let imp = crate::skills::skill_impl(skill_id)?;
    imp.castend_pos2(c, src, x, y, skill_lv, tick, flag);
    Some(())
}

/// clif.cpp clif_skill_damage(src, dst, tick, sdelay, ddelay, sdamage, div, skill_id, skill_lv, type) (skillcore:
/// skill/clif.rs).
#[allow(clippy::too_many_arguments)]
pub fn clif_skill_damage(c: &Ctx, src: &Unit, dst: &Unit, tick: Tick, sdelay: i32, ddelay: i32, sdamage: i64, div: i16, skill_id: u16, skill_lv: u16, kind: u8) {
    crate::skill::clif::clif_skill_damage(c, src, dst, tick, sdelay, ddelay, sdamage, div, skill_id, skill_lv as i32, kind);
}

/// sd->skillitem / sd->skillitemlv / sd->skillitem_keep_requirement (skillcore: skill/state.rs PcSkill).
pub fn skillitem(c: &Ctx, aid: u32) -> (u16, u16, bool) {
    let s = crate::skill::state::get(c, aid);
    (s.skillitem as u16, s.skillitemlv as u16, s.skillitem_keep_requirement)
}

/// skill_castend_map: `sd->skillitem = sd->skillitemlv = sd->skillitem_keep_requirement = 0` (skillcore's PcSkill).
pub fn clear_skillitem(c: &Ctx, aid: u32) {
    crate::skill::state::with(c, aid, |s| {
        s.skillitem = 0;
        s.skillitemlv = 0;
        s.skillitem_keep_requirement = false;
    });
}

/// sd->state.arrow_atk (battle/state.rs: set by the normal attack and by skill_check_condition_castend).
pub fn arrow_atk(c: &Ctx, aid: u32) -> bool {
    crate::battle::state::get(c, aid).arrow_atk
}

/// sd->canskill_tick = gettick() (skillcore's PcSkill).
pub fn set_canskill_tick(c: &Ctx, aid: u32, tick: Tick) {
    crate::skill::state::with(c, aid, |s| s.canskill_tick = tick);
}

/// sd->state.autocast (skillcore's PcSkill: autospell / item casts).
pub fn autocast(c: &Ctx, aid: u32) -> bool {
    crate::skill::state::get(c, aid).autocast
}

/// `sc->comet_x = x; sc->comet_y = y` (skill_unitsetting of NPC_COMET / WL_COMET): status_change's members
/// (status/data.rs ScUnit), read by the comet's damage calculation.
pub fn set_comet_pos(c: &Ctx, bl_id: u32, x: i16, y: i16) {
    let mut us = crate::status::sc::unit_sc(c, bl_id);
    us.comet_x = x;
    us.comet_y = y;
    crate::status::sc::put_unit_sc(c, &us);
}

/// sd->message: the text of a Talkie Box / Graffiti (clif_parse_UseSkillToPosMoreInfo sets it through skillcore's
/// skill/iface.rs set_sd_message; the field lives in buyingstore.rs's row, which vending shares). Empty until set, as a
/// fresh map_session_data's is.
pub fn skill_message(c: &Ctx, aid: u32) -> String {
    crate::buyingstore::bs(c, aid).message
}

/// mob.cpp mobskill_event(md, src, tick, flag) (damage = 0, the default argument; skillcore: mob/skill.rs).
pub fn mobskill_event(c: &Ctx, md: &Unit, src: &Unit, tick: Tick, flag: i32) {
    crate::mob::skill::mobskill_event(c, md.id, src, tick, flag, 0);
}

// ---------------------------------------------------------------------------------------------- status2

/// status.cpp status_change_clear_buffs(bl, type) (status2 agent: crate::status::sc_clear::status_change_clear_buffs).
pub fn status_change_clear_buffs(c: &Ctx, bl: &Unit, kind: u8) {
    crate::status::sc_clear::status_change_clear_buffs(c, bl.id, kind);
}

/// status.hpp e_status_change_clear_buffs_flags SCCB_* (status2 agent's sc_clear).
pub use crate::status::sc_clear::{SCCB_BUFFS, SCCB_CHEM_PROTECT, SCCB_DEBUFFS, SCCB_HERMODE, SCCB_LUXANIMA, SCCB_REFRESH};

// ---------------------------------------------------------------------------------------------- battlegrounds

/// battleground.cpp bg_team_get_id (battleground.rs).
pub fn bg_team_get_id(c: &Ctx, bl: &Unit) -> i32 {
    crate::battleground::bg_team_get_id(c, bl)
}
