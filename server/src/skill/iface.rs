//! What the skill pipeline calls in the systems other wave-D agents own, at one place: each function names its owner
//! and the rAthena function; until that agent's commit is merged here it is a commented stub whose answer is the one
//! rAthena gives when that system has nothing (no skill unit exists, no mercenary is out). The merge replaces the
//! bodies with calls into the owner's module.
//!
//! - skillunit agent (server/ARCHITECTURE.md): skill units and their groups (`crate::skillunit`): skill_castend_pos2's shell,
//!   skill_pos_maxcount_check, skill_check_unit_range(2), unit_skillunit_maxcount, skill_check_pc_partner,
//!   skill_castend_song, map_find_skill_unit_oncell. Forwarded (skillunit branch, after merging skillcore d22a995).
//! - status2 agent: skill_sit (status/sc_ext). Forwarded (status2 branch, after merging skillcore d22a995).
//! - companions (pets: pet agent; homunculi: homunculus/ and skill/homun.rs; mercenaries / elementals: mercenary/,
//!   elemental/, companion/).

use spacetimedb::Table;

use crate::unit::{unit as _, Unit};
use crate::world::{Ctx, Tick};

/// skill.cpp skill_castend_pos2 (skillunit agent: crate::skillunit::castend, the shell that calls
/// `skill->impl->castendPos2`).
#[allow(clippy::too_many_arguments)]
pub fn skill_castend_pos2(c: &Ctx, src: &Unit, x: i16, y: i16, skill_id: u16, skill_lv: u16, tick: Tick, flag: i32) -> i32 {
    crate::skillunit::skill_castend_pos2(c, src, x, y, skill_id, skill_lv, tick, flag)
}

/// skill.cpp skill_pos_maxcount_check (skillunit agent: crate::skillunit::castend).
#[allow(clippy::too_many_arguments)]
pub fn skill_pos_maxcount_check(c: &Ctx, src: &Unit, x: i16, y: i16, skill_id: u16, skill_lv: u16, type_: u16, display_failure: bool) -> bool {
    crate::skillunit::skill_pos_maxcount_check(c, src, x, y, skill_id, skill_lv, type_, display_failure)
}

/// skill.cpp skill_isNotOk_npcRange → skill_check_unit_range2(…, isNearNPC = true) (skillunit agent:
/// crate::skillunit::castend): true when an NPC is too near.
pub fn skill_is_not_ok_npc_range(c: &Ctx, src: &Unit, skill_id: u16, skill_lv: u16, x: i16, y: i16) -> bool {
    crate::skillunit::castend::skill_is_not_ok_npc_range(c, src, skill_id, skill_lv, x, y)
}

/// skill.cpp skill_check_pc_partner (skillunit agent: crate::skillunit::castend): the ensemble partners around a
/// player (0 = none).
pub fn skill_check_pc_partner(c: &Ctx, sd: &Unit, skill_id: u16, skill_lv: &mut u16, range: i32, cast_flag: i32) -> i32 {
    crate::skillunit::skill_check_pc_partner(c, sd.id, skill_id, skill_lv, range, cast_flag)
}

/// A BL_SKILL unit's group is a trap (`su->group && skill_get_inf2(su->group->skill_id, INF2_ISTRAP)`; skillunit).
pub fn skill_unit_group_is_trap(c: &Ctx, su: &Unit) -> bool {
    crate::skillunit::group_of_unit(c, su.id).map(|g| crate::db::skills::skill_get_inf2(g.skill_id, &[crate::db::skills::INF2_ISTRAP])).unwrap_or(false)
}

/// skill_attack_blow's Blast Mine rule: `if (su->group && su->group->skill_id == HT_BLASTMINE) skill_blown(src,
/// target, 3, -1, BLOWN_NONE);` (skillunit agent: the unit's group).
pub fn blastmine_knockback(c: &Ctx, src: &Unit, target: &Unit) {
    if crate::skillunit::group_of_unit(c, target.id).map(|g| g.skill_id == crate::db::skills::HT_BLASTMINE).unwrap_or(false) {
        super::attack::skill_blown(c, src, target, 3, -1, crate::skillunit::consts::BLOWN_NONE);
    }
}

/// A mercenary's master (`((TBL_MER*)bl)->master->id`; mercenary/data.rs). None: no such mercenary / master.
pub fn mercenary_master_id(c: &Ctx, mer: &Unit) -> Option<u32> {
    crate::mercenary::data::get(c, mer.id).map(|md| md.master).filter(|&m| crate::pc::get(c, m).is_some())
}

/// skill.cpp skill_sit (status2 agent, status/sc_ext): Gangster's Paradise / Taekwon rest areas on sit / stand.
pub fn skill_sit(c: &Ctx, aid: u32, sitting: bool) -> i32 {
    crate::status::sc_ext::skill_sit(c, aid, sitting)
}

/// skill.cpp skill_unitsetting (skillunit agent: crate::skillunit::setting): places a skill's unit group; false when
/// rAthena's returns nullptr.
#[allow(clippy::too_many_arguments)]
pub fn skill_unitsetting(c: &Ctx, src: &Unit, skill_id: u16, skill_lv: u16, x: i16, y: i16, flag: i32) -> bool {
    crate::skillunit::skill_unitsetting(c, src, skill_id, skill_lv, x, y, flag).is_some()
}

/// map_foreachin(all)area(skill_cell_overlap, …, BL_SKILL, skill_id, &dummy, src) (skillunit agent:
/// crate::skillunit::castend).
#[allow(clippy::too_many_arguments)]
pub fn skill_cell_overlap_area(c: &Ctx, src: &Unit, m: u16, x0: i16, y0: i16, x1: i16, y1: i16, skill_id: u16, wall: super::area::Wall) -> i32 {
    crate::skillunit::castend::skill_cell_overlap_area(c, src, m, x0, y0, x1, y1, skill_id, wall)
}

/// skill_timerskill's WZ_WATERBALL: the next Water Ball cell (skill_get_unit_layout + map_find_skill_unit_oncell
/// from slot `start`); the unit's id (skillunit agent: crate::skillunit::castend).
pub fn waterball_next_unit(c: &Ctx, src: &Unit, skill_lv: u16, x: i16, y: i16, start: i32) -> Option<u32> {
    crate::skillunit::castend::skill_waterball_next_unit(c, src, skill_lv, x, y, start)
}

/// skill.cpp skill_delunit (skillunit agent: crate::skillunit::group). 0 is no unit (`if( !unit || !unit->alive )
/// return 0;`).
pub fn skill_delunit(c: &Ctx, unit_id: u32) -> i32 {
    if unit_id == 0 {
        return 0;
    }
    crate::skillunit::skill_delunit(c, unit_id)
}

/// map_getcell(m, x, y, CELL_CHKLANDPROTECTOR) (map_cell.rs; skillunit sets it for Land Protector).
pub fn map_getcell_landprotector(c: &Ctx, m: u16, x: i16, y: i16) -> bool {
    crate::map_cell::getcell(c, Some(m), x, y, crate::map_cell::CELL_CHKLANDPROTECTOR) != 0
}

/// map_getcell(m, x, y, CELL_CHKMAELSTROM) (map_cell.rs; skillunit sets it for Maelstrom).
pub fn map_getcell_maelstrom(c: &Ctx, m: u16, x: i16, y: i16) -> bool {
    crate::map_cell::getcell(c, Some(m), x, y, crate::map_cell::CELL_CHKMAELSTROM) != 0
}

/// skill_castend_id_check's trap rule: a BL_SKILL target without a group, or whose group is a trap
/// (`!su->group || skill_get_inf2(su->group->skill_id, INF2_ISTRAP)`; skillunit agent).
pub fn skill_unit_target_is_trap_or_groupless(c: &Ctx, su: &Unit) -> bool {
    crate::skillunit::group_of_unit(c, su.id).map(|g| crate::db::skills::skill_get_inf2(g.skill_id, &[crate::db::skills::INF2_ISTRAP])).unwrap_or(true)
}

/// skill_castend_id_check's SKE_STAR_BURST / SKE_STAR_CANNON rule: one of the caster's unit groups of `skill_id` has
/// its unit within the group's unit range of the caster (ud->skillunits; skillunit agent: crate::skillunit::castend).
pub fn caster_has_unit_in_range(c: &Ctx, src: &Unit, skill_id: u16) -> bool {
    crate::skillunit::castend::skill_caster_unit_in_range(c, src, skill_id)
}

/// skill.cpp skill_check_unit_range (skillunit agent: crate::skillunit::castend): a unit of a skill that must not
/// stack is in the area.
pub fn skill_check_unit_range(c: &Ctx, src: &Unit, x: i16, y: i16, skill_id: u16, skill_lv: u16) -> bool {
    crate::skillunit::castend::skill_check_unit_range(c, src, x, y, skill_id, skill_lv) != 0
}

/// skill.cpp skill_check_condition_mercenary (skill.cpp:3552): the one port, skill/homun.rs (BL_HOM and BL_MER
/// masters).
pub fn skill_check_condition_mercenary(c: &Ctx, bl: &Unit, skill_id: u16, skill_lv: u16, type_: i32) -> bool {
    super::homun::skill_check_condition_mercenary(c, bl, skill_id, skill_lv, type_)
}

/// sd->npc_item_flag (script/npc_run.rs's `pc_script` row): the NPC dialog was opened by an item.
pub fn npc_item_flag(c: &Ctx, aid: u32) -> bool {
    use crate::script::npc_run::pc_script as _;
    c.db().pc_script().account_id().find(aid).map(|s| s.npc_item_flag).unwrap_or(false)
}

/// sd->message = msg (rAthena's one field; the module keeps it in buyingstore.rs's `pc_buyingstore` row, which the
/// vending system shares).
pub fn set_sd_message(c: &Ctx, aid: u32, msg: &str) {
    use crate::buyingstore::pc_buyingstore as _;
    let mut b = crate::buyingstore::bs(c, aid);
    b.message = msg.to_string();
    if c.db().pc_buyingstore().account_id().find(aid).is_some() {
        c.db().pc_buyingstore().account_id().update(b);
    } else {
        c.db().pc_buyingstore().insert(b);
    }
}

/// map_getcell(m, x, y, CELL_CHKNOVENDING) (map_cell.rs: setcell's cell flags).
pub fn map_getcell_novending(c: &Ctx, m: u16, x: i16, y: i16) -> bool {
    crate::map_cell::getcell(c, Some(m), x, y, crate::map_cell::CELL_CHKNOVENDING) != 0
}

/// map_getcell(m, x, y, CELL_CHKNOBUYINGSTORE) (map_cell.rs).
pub fn map_getcell_nobuyingstore(c: &Ctx, m: u16, x: i16, y: i16) -> bool {
    crate::map_cell::getcell(c, Some(m), x, y, crate::map_cell::CELL_CHKNOBUYINGSTORE) != 0
}

/// skill.cpp skill_unitsetting (skillunit's port) for the callers that keep the group it returns (`sg->group_id`:
/// LG_BANDING, PA_GOSPEL). None: nullptr (no group placed).
#[allow(clippy::too_many_arguments)]
pub fn skill_unitsetting_group_id(c: &Ctx, src: &Unit, skill_id: u16, skill_lv: u16, x: i16, y: i16, flag: i32) -> Option<i32> {
    crate::skillunit::skill_unitsetting(c, src, skill_id, skill_lv, x, y, flag)
}

/// A BL_SKILL unit's `su->alive && (sg = su->group)`: its group's (skill_id, skill_lv, unit_id, bl_flag) (skillunit's
/// data; skills/swordman/trample.cpp skill_destroy_trap). None: not a live skill unit with a group.
pub fn skill_unit_alive_group(c: &Ctx, su: &Unit) -> Option<(u16, u16, u16, i32)> {
    let unit = crate::skillunit::data::get_unit(c, su.id)?;
    if !unit.alive {
        return None;
    }
    let g = crate::skillunit::skill_id2group(c, unit.group_id)?;
    Some((g.skill_id, g.skill_lv, g.unit_id as u16, g.bl_flag))
}

/// skill.cpp skill_trap_splash(bl, va_list{ su, tick }) (skillunit's port): a trap's effect on `bl`.
pub fn skill_trap_splash(c: &Ctx, bl: &Unit, su: &Unit, tick: Tick) -> i32 {
    crate::skillunit::ontimer::skill_trap_splash(c, bl, su.id, tick)
}

/// status.cpp status_change_clear_buffs(bl, type) (status2's port, status/sc_clear).
pub fn status_change_clear_buffs(c: &Ctx, bl: &Unit, type_: u8) {
    crate::status::sc_clear::status_change_clear_buffs(c, bl.id, type_);
}

/// map_foreachinmap(skill_graffitiremover, m, BL_SKILL, 0) (skill_check_condition_castbegin's RG_GRAFFITI): the
/// Graffiti units on map m (skillunit's skill_graffitiremover counts one per unit; the order does not matter for a sum).
pub fn skill_graffiti_count(c: &Ctx, m: u16) -> i32 {
    let units: Vec<Unit> = c.db().unit().by_map_kind().filter((m, crate::map::BL_SKILL)).collect();
    units.iter().map(|u| crate::skillunit::ontimer::skill_graffitiremover(c, u, 0)).sum()
}

/// sd->duel_group (duel.rs; skill_check_condition_castbegin's AL_WARP).
pub fn duel_group(c: &Ctx, aid: u32) -> u32 {
    crate::duel::duel_group(c, aid)
}

/// sd->feel_map[i].m (skill_check_condition_castbegin's SG_*_COMFORT, castend's SG_*_WARM): the Star Gladiator's
/// Feeling map (crate::pc_taekwon, the one owner of the feel / hate fields), None for -1 (no map).
pub fn sg_feel_map(c: &Ctx, aid: u32, i: usize) -> Option<u16> {
    let m = crate::pc_taekwon::feel_map(c, aid, i);
    (m >= 0).then_some(m as u16)
}

/// A BL_SKILL unit's group skill (`skill_unit* unit = (skill_unit*)bl; unit->group ? unit->group->skill_id`;
/// skillunit's data; skill.cpp skill_attack_area's SS_FUUMAKOUCHIKU rule, skills/npc/acidbreath.rs). None:
/// `unit->group == nullptr` (or not a skill unit).
pub fn skill_unit_group_skill_id(c: &Ctx, su: &Unit) -> Option<u16> {
    crate::skillunit::group_of_unit(c, su.id).map(|g| g.skill_id)
}
