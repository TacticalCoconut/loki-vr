//! Script commands of the skill pipeline (script.cpp, rAthena e985006): itemskill, skilleffect, npcskilleffect,
//! unitskilluseid, unitskillusepos, areamobuseskill and npcskill; and the spirit sphere commands addspiritball,
//! delspiritball and countspiritball (over pc_addspiritball / pc_delspiritball of skill/state.rs).

use super::script2::unitctl::{map_id2bl, script_rid2bl, Bl};
use super::*;
use crate::db::skills::{skill_db, skill_get_casttype, skill_get_castcancel, skill_get_hit, skill_get_index, skill_name2id, CastType};
use crate::map::{BL_MOB, BL_NPC, BL_PC};
use crate::skill::clif::{clif_item_skill, clif_skill_damage, clif_skill_nodamage, clif_skill_poseffect};
use crate::unit::Unit;
use crate::world::Ctx;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "itemskill" => itemskill,
        "skilleffect" => skilleffect,
        "npcskilleffect" => npcskilleffect,
        "unitskilluseid" => unitskilluseid,
        "unitskillusepos" => unitskillusepos,
        "areamobuseskill" => areamobuseskill,
        "npcskill" => npcskill,
        "addspiritball" => addspiritball,
        "delspiritball" => delspiritball,
        "countspiritball" => countspiritball,
        _ => return None,
    })
}

/// The skill named or numbered by argument 3 of unitskilluseid / unitskillusepos: skill_name2id of a string (0:
/// "Invalid skill name"), else the number when skill_get_index knows it (else "Invalid skill ID"). None ends the
/// command with SCRIPT_CMD_FAILURE after rAthena's ShowError.
fn skill_arg(rt: &Rt, st: &mut State, func: &str, name_err: &str, id_err: &str) -> Option<u16> {
    if isstring(rt, st, 3) {
        let name = lat(&getstr(rt, st, 3));
        let id = skill_name2id(&name);
        if id == 0 {
            log::error!("{func}: Invalid skill name {name}{name_err}");
            return None;
        }
        Some(id)
    } else {
        // skill_id = script_getnum(st, 3): the uint16 the int converts to
        let id = getnum(rt, st, 3) as u16;
        if skill_get_index(id) == 0 {
            log::error!("{func}: Invalid skill ID {id}{id_err}");
            return None;
        }
        Some(id)
    }
}

/// BUILDIN_FUNC(itemskill): itemskill <skill id>,<level>{,<keep requirement>}; / itemskill "<skill name>",<level>...
/// Nothing while the attached player casts (ud.skilltimer); else sd->skillitem / skillitemlv /
/// skillitem_keep_requirement are set and clif_item_skill offers the skill.
fn itemskill(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = rid2sd(rt, st, "buildin_itemskill") else { return SCRIPT_CMD_SUCCESS };
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(sd) = crate::unit::get(c, aid) else { return SCRIPT_CMD_SUCCESS };
    if sd.ud.skilltimer != 0 {
        return SCRIPT_CMD_SUCCESS;
    }
    // int32 id: a string gives skill_name2id, a number is kept as the int it is
    let id: i32 = if isstring(rt, st, 2) {
        let name = lat(&getstr(rt, st, 2));
        let id = skill_name2id(&name);
        if id == 0 {
            log::error!("buildin_itemskill: Invalid skill name {name} passed to item bonus. Skipping.");
            return SCRIPT_CMD_FAILURE;
        }
        id as i32
    } else {
        let id = getnum(rt, st, 2);
        if skill_get_index(id as u16) == 0 {
            log::error!("buildin_itemskill: Invalid skill ID {id} passed to item bonus. Skipping.");
            return SCRIPT_CMD_FAILURE;
        }
        id
    };
    let lv = getnum(rt, st, 3);
    let keep_requirement = if st.hasdata(4) { getnum(rt, st, 4) != 0 } else { false };
    // sd->skillitem / skillitemlv are int16
    crate::skill::state::with(c, aid, |s| {
        s.skillitem = id as i16;
        s.skillitemlv = lv as i16;
        s.skillitem_keep_requirement = keep_requirement;
    });
    // clif_item_skill(sd, id, lv): uint16 parameters
    clif_item_skill(c, &sd, id as u16, lv as u16);
    SCRIPT_CMD_SUCCESS
}

/// script.cpp script_skill_effect: the skill's animation on the unit itself by its cast type: ground skills
/// clif_skill_poseffect at (x, y), support skills clif_skill_nodamage (heal = skill_lv), damage skills
/// clif_skill_damage with the unit's amotion / dmotion, no damage, one hit and skill_get_hit.
fn script_skill_effect(c: &Ctx, bl: &Unit, skill_id: u16, skill_lv: u16, x: i16, y: i16) {
    match skill_get_casttype(skill_id) {
        CastType::Ground => clif_skill_poseffect(c, bl, skill_id, skill_lv, x, y, c.tick()),
        CastType::NoDamage => {
            clif_skill_nodamage(c, Some(bl), bl, skill_id, skill_lv as i32, true);
        }
        CastType::Damage => {
            let st = crate::battle::bl::status(c, bl);
            clif_skill_damage(c, bl, bl, c.tick(), st.amotion as i32, st.dmotion as i32, 0, 1, skill_id, skill_lv as i32, skill_get_hit(skill_id));
        }
    }
}

/// BUILDIN_FUNC(skilleffect): skilleffect <skill id>,<level>{,<game id>}; / skilleffect "<skill name>",<level>...
/// The skill's animation on the unit (the attached player by default). A sitting player stands first (the packet
/// makes the client stand the character): pc_setstand, skill_sit(sd, 0) and clif_standing.
fn skilleffect(rt: &Rt, st: &mut State) -> i32 {
    let bl = if st.hasdata(4) {
        match script_rid2bl(rt, st, 4, "buildin_skilleffect") {
            Some(bl) => bl,
            None => return SCRIPT_CMD_SUCCESS,
        }
    } else {
        // target is the character attached (default)
        let Some(aid) = rid2sd(rt, st, "buildin_skilleffect") else { return SCRIPT_CMD_SUCCESS };
        let Some(u) = rt.c.and_then(|c| crate::unit::get(c, aid)) else { return SCRIPT_CMD_SUCCESS };
        Bl::Unit(u)
    };
    let skill_id: u16 = if isstring(rt, st, 2) { skill_name2id(&lat(&getstr(rt, st, 2))) } else { getnum(rt, st, 2) as u16 };
    if skill_db().find(skill_id).is_none() {
        log::error!("buildin_skilleffect: Invalid skill defined ({})!", lat(&getstr(rt, st, 2)));
        return SCRIPT_CMD_FAILURE;
    }
    let skill_lv = getnum(rt, st, 3) as u16;
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Bl::Unit(u) = bl else {
        // a floating NPC is on no map (m < 0): map_foreachinallarea finds nobody, so the packets reach no one
        return SCRIPT_CMD_SUCCESS;
    };
    if u.kind == BL_PC {
        // Ensure we're standing because the following packet causes the client to virtually set the char to stand,
        // which leaves the server thinking it still is sitting.
        if crate::inventory::pc_issit(c, u.id) && crate::pc::setstand(c, u.id, false) {
            crate::skill::iface::skill_sit(c, u.id, false);
            if let Some(now) = crate::unit::get(c, u.id) {
                crate::clif::standing(c, &now);
            }
        }
    }
    // bl->x, bl->y as they are now
    let Some(u) = crate::unit::get(c, u.id) else { return SCRIPT_CMD_SUCCESS };
    script_skill_effect(c, &u, skill_id, skill_lv, u.x, u.y);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(npcskilleffect): npcskilleffect <skill id>,<level>,<x>,<y>; the skill's animation on the NPC the
/// script runs on (st->oid). rAthena reads x and y and then plays the effect at the NPC's own cell.
fn npcskilleffect(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(bl) = map_id2bl(c, st.oid) else {
        log::error!("buildin_npcskilleffect: Invalid object attached to NPC.");
        return SCRIPT_CMD_FAILURE;
    };
    let skill_id: u16 = if isstring(rt, st, 2) { skill_name2id(&lat(&getstr(rt, st, 2))) } else { getnum(rt, st, 2) as u16 };
    let skill_lv = getnum(rt, st, 3) as u16;
    let _x = getnum(rt, st, 4) as i16;
    let _y = getnum(rt, st, 5) as i16;
    if skill_db().find(skill_id).is_none() {
        log::error!("buildin_npcskilleffect: Invalid skill defined ({})!", lat(&getstr(rt, st, 2)));
        return SCRIPT_CMD_FAILURE;
    }
    if let Bl::Unit(u) = bl {
        script_skill_effect(c, &u, skill_id, skill_lv, u.x, u.y);
    }
    // a floating NPC is on no map: nobody receives the packets
    SCRIPT_CMD_SUCCESS
}

/// The part unitskilluseid and unitskillusepos share after reading their arguments: a Line_ID message (monsters
/// only: mob_chat_display_message; else "Msg can only be used for monster." and SCRIPT_CMD_FAILURE, rAthena's text
/// names unitskilluseid in both commands) and status_calc_npc for an NPC (SCO_FIRST while its hp is 0). Returns
/// false when the command must fail.
fn unitskilluse_prepare(c: &Ctx, bl: &Bl, msg_id: i32) -> bool {
    if msg_id > 0 {
        if bl.kind() != BL_MOB {
            log::error!("buildin_unitskilluseid: Msg can only be used for monster.");
            return false;
        }
        // map_id2md(bl->id)
        if crate::mob::get(c, bl.id()).is_some() {
            crate::mob::skill::mob_chat_display_message(c, bl.id(), msg_id as u16);
        }
    }
    if bl.kind() == BL_NPC {
        super::script2::unitinfo::npc_status_calc_first_or_none(c, bl.id());
    }
    true
}

/// BUILDIN_FUNC(unitskilluseid): unitskilluseid <unit_id>,<skill_id>,<skill_lv>{,<target_id>,<casttime>,<cancel>,
/// <Line_ID>,<ignore_range>}; the unit casts the skill on the target (default: the unit id argument itself) with
/// casttime seconds added to skill_castfix; cancel defaults to skill_get_castcancel.
fn unitskilluseid(rt: &Rt, st: &mut State) -> i32 {
    let unit_id = getnum(rt, st, 2);
    let Some(skill_id) = skill_arg(rt, st, "buildin_unitskilluseid", " passed to item bonus. Skipping.", " passed to item bonus. Skipping.") else {
        return SCRIPT_CMD_FAILURE;
    };
    let skill_lv = getnum(rt, st, 4) as u16;
    let target_id = if st.hasdata(5) { getnum(rt, st, 5) } else { unit_id };
    let casttime = if st.hasdata(6) { getnum(rt, st, 6) } else { 0 };
    let cancel = if st.hasdata(7) { getnum(rt, st, 7) > 0 } else { skill_get_castcancel(skill_id) != 0 };
    let msg_id = if st.hasdata(8) { getnum(rt, st, 8) } else { 0 };
    let ignore_range = if st.hasdata(9) { getnum(rt, st, 9) > 0 } else { false };
    if let Some(bl) = script_rid2bl(rt, st, 2, "buildin_unitskilluseid") {
        let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
        if !unitskilluse_prepare(c, &bl, msg_id) {
            return SCRIPT_CMD_FAILURE;
        }
        // a floating NPC has no Unit row: unit_skilluse_id2 finds no caster on a map (it returns 0 in rAthena too:
        // the NPC is on no map)
        if let Some(u) = crate::unit::get(c, bl.id()) {
            let time = casttime.wrapping_mul(1000).wrapping_add(crate::skill::castfix::skill_castfix(c, &u, skill_id, skill_lv));
            crate::skill::unit::unit_skilluse_id2(c, u.id, target_id as u32, skill_id, skill_lv, time, cancel as i32, ignore_range);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(unitskillusepos): unitskillusepos <unit_id>,<skill_id>,<skill_lv>,<target_x>,<target_y>{,<casttime>,
/// <cancel>,<Line_ID>,<ignore_range>}; the unit casts the ground skill at (x, y).
fn unitskillusepos(rt: &Rt, st: &mut State) -> i32 {
    let Some(skill_id) = skill_arg(rt, st, "buildin_unitskillusepos", ".", ".") else {
        return SCRIPT_CMD_FAILURE;
    };
    let skill_lv = getnum(rt, st, 4) as u16;
    let skill_x = getnum(rt, st, 5);
    let skill_y = getnum(rt, st, 6);
    let casttime = if st.hasdata(7) { getnum(rt, st, 7) } else { 0 };
    let cancel = if st.hasdata(8) { getnum(rt, st, 8) > 0 } else { skill_get_castcancel(skill_id) != 0 };
    let msg_id = if st.hasdata(9) { getnum(rt, st, 9) } else { 0 };
    let ignore_range = if st.hasdata(10) { getnum(rt, st, 10) > 0 } else { false };
    if let Some(bl) = script_rid2bl(rt, st, 2, "buildin_unitskillusepos") {
        let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
        if !unitskilluse_prepare(c, &bl, msg_id) {
            return SCRIPT_CMD_FAILURE;
        }
        if let Some(u) = crate::unit::get(c, bl.id()) {
            let time = casttime.wrapping_mul(1000).wrapping_add(crate::skill::castfix::skill_castfix(c, &u, skill_id, skill_lv));
            // unit_skilluse_pos2 takes int16 coordinates
            crate::skill::unit::unit_skilluse_pos2(c, u.id, skill_x as i16, skill_y as i16, skill_id, skill_lv, time, cancel as i32, ignore_range);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// script.cpp buildin_mobuseskill_sub: one monster of class `mobid` in the area uses the skill on its target (0 itself,
/// 1 its target, 2 its master, else a random enemy in the skill's range), cancelling a cast in progress, then shows
/// the emotion.
#[allow(clippy::too_many_arguments)]
fn mobuseskill_sub(c: &Ctx, bl: &Unit, mobid: i32, skill_id: u16, skill_lv: u16, casttime: i32, cancel: i32, emotion: i32, target: i32) -> i32 {
    let Some(md) = crate::mob::get(c, bl.id) else { return 0 };
    if md.mob_id != mobid {
        return 0;
    }
    // 0:self, 1:target, 2:master, default:random
    let tbl = match target {
        0 => crate::unit::get(c, bl.id),
        1 => crate::unit::get(c, md.target_id),
        2 => crate::unit::get(c, md.master_id),
        _ => {
            // DEFAULT_ENEMY_TYPE(md)
            let kinds = if md.special_ai != 0 { crate::map::BL_CHAR } else { BL_MOB | BL_PC | crate::map::BL_HOM | crate::map::BL_MER };
            let range = crate::skill::misc::skill_get_range2(c, bl, skill_id, skill_lv, true);
            crate::battle::target::battle_getenemy(c, bl, kinds, range)
        }
    };
    let Some(tbl) = tbl else { return 0 };
    if bl.ud.skilltimer != 0 {
        // Cancel the casting skill.
        crate::skill::unit::unit_skillcastcancel(c, bl.id, 0);
    }
    if skill_get_casttype(skill_id) == CastType::Ground {
        crate::skill::unit::unit_skilluse_pos2(c, bl.id, tbl.x, tbl.y, skill_id, skill_lv, casttime, cancel, false);
    } else {
        crate::skill::unit::unit_skilluse_id2(c, bl.id, tbl.id, skill_id, skill_lv, casttime, cancel, false);
    }
    if let Some(u) = crate::unit::get(c, bl.id) {
        crate::clif::emotion(c, &u, emotion as u8);
    }
    1
}

/// BUILDIN_FUNC(areamobuseskill): areamobuseskill "<map>",<x>,<y>,<range>,"<mob name>"/<mob id>,"<skill name>"/<skill
/// id>,<skill lv>,<cast time>,<cancelable>,<emotion>,<target type>; every monster of the class within `range` of the
/// cell (map_foreachinallrange, no wall check) uses the skill (buildin_mobuseskill_sub).
fn areamobuseskill(rt: &Rt, st: &mut State) -> i32 {
    let mapn = lat(&getstr(rt, st, 2));
    let Some(m) = crate::data::maps().id_of(&mapn) else {
        log::error!("areamobuseskill: invalid map name.");
        return SCRIPT_CMD_SUCCESS;
    };
    let x = getnum(rt, st, 3) as i16;
    let y = getnum(rt, st, 4) as i16;
    let range = getnum(rt, st, 5);
    let mobid: u16 = if isstring(rt, st, 6) {
        let name = lat(&getstr(rt, st, 6));
        let Some(mob) = crate::db::mobs::mobdb_search_aegisname(&name) else {
            log::warn!("buildin_areamobuseskill: Attempted to use skill of non-existing monster \"{name}\"");
            return SCRIPT_CMD_FAILURE;
        };
        mob.id as u16
    } else {
        let id = getnum(rt, st, 6) as u16;
        if crate::db::mobs::mob_db().find(id as u32).is_none() {
            log::warn!("buildin_areamobuseskill: Attempted to use skill of non-existing monster class {id}");
            return SCRIPT_CMD_FAILURE;
        }
        id
    };
    let skill_id: i32 = if isstring(rt, st, 7) {
        let name = lat(&getstr(rt, st, 7));
        let id = skill_name2id(&name);
        if id == 0 {
            log::error!("buildin_areamobuseskill: Invalid skill name {name}.");
            return SCRIPT_CMD_FAILURE;
        }
        id as i32
    } else {
        let id = getnum(rt, st, 7);
        if skill_get_index(id as u16) == 0 {
            log::error!("buildin_areamobuseskill: Invalid skill ID {id}.");
            return SCRIPT_CMD_FAILURE;
        }
        id
    };
    let mut skill_lv = getnum(rt, st, 8);
    if skill_lv > crate::skill::conf::mob_max_skilllvl() {
        skill_lv = crate::skill::conf::mob_max_skilllvl();
    }
    let casttime = getnum(rt, st, 9);
    let cancel = getnum(rt, st, 10);
    let emotion = getnum(rt, st, 11);
    let target = getnum(rt, st, 12);
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    // map_foreachinallrange(…, &center, range, BL_MOB, …): the int16 range box around the cell
    let r = range as i16;
    crate::skill::area::foreach_in_area(c, m, x - r, y - r, x + r, y + r, BL_MOB, crate::skill::area::Wall::None, |c, bl| {
        mobuseskill_sub(c, bl, mobid as i32, skill_id as u16, skill_lv as u16, casttime, cancel, emotion, target)
    });
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- npcskill

/// battle_config.max_third_parameter: conf/battle/player.conf `max_third_parameter: 130` (npcskill's stat point cap).
pub fn max_third_parameter() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MAX_THIRD_PARAMETER)
}
/// map.hpp MAX_LEVEL.
pub const MAX_LEVEL: u32 = 275;

/// npcskill's checks of its stat point and NPC level, both uint32 (a negative argument wraps and fails): `stat_point >
/// battle_config.max_third_parameter` compares as unsigned. Err carries rAthena's message.
pub fn npcskill_check(stat_point: u32, npc_level: u32) -> Result<(), String> {
    if stat_point > max_third_parameter() as u32 {
        return Err(format!("npcskill: stat point exceeded maximum of {}.", max_third_parameter()));
    }
    if npc_level > MAX_LEVEL {
        return Err(format!("npcskill: level exceeded maximum of {MAX_LEVEL}."));
    }
    Ok(())
}

/// BUILDIN_FUNC(npcskill) (script.cpp:23286-23341): npcskill <skill id>|"<skill name>",<skill lv>,<stat point>,<NPC
/// level>; — the NPC the attached player talks to (sd->npc_id) casts the skill on the player (a ground skill at the
/// player's cell): its nd->level and nd->stat_point are set, status_calc_npc runs (SCO_FIRST while its hp is 0), then
/// unit_skilluse_pos2 / unit_skilluse_id2 with no cast time, no cast cancel and the range ignored. Without a player:
/// nothing (the script ends). An unknown skill, a stat point over max_third_parameter, a level over MAX_LEVEL or no
/// NPC: SCRIPT_CMD_FAILURE. A floating NPC is on no map: the skill use fails there (unit_skilluse_*2 find no caster
/// on a map, as rAthena's check `src->prev`).
fn npcskill(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = rid2sd(rt, st, "buildin_npcskill") else { return SCRIPT_CMD_SUCCESS };
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let skill_id: u16 = if isstring(rt, st, 2) {
        let name = lat(&getstr(rt, st, 2));
        let id = skill_name2id(&name);
        if id == 0 {
            log::error!("buildin_npcskill: Invalid skill name {name}.");
            return SCRIPT_CMD_FAILURE;
        }
        id
    } else {
        // skill_id = script_getnum(st, 2): the uint16 the int converts to
        let id = getnum(rt, st, 2) as u16;
        if skill_get_index(id) == 0 {
            log::error!("buildin_npcskill: Invalid skill ID {id}.");
            return SCRIPT_CMD_FAILURE;
        }
        id
    };
    let skill_level = getnum(rt, st, 3) as u16;
    let stat_point = getnum(rt, st, 4) as u32;
    let npc_level = getnum(rt, st, 5) as u32;
    let npc_id = crate::pc::get(c, aid).map(|p| p.npc_id).unwrap_or(0);
    let nd = map_id2bl(c, npc_id);
    if let Err(e) = npcskill_check(stat_point, npc_level) {
        log::error!("{e}");
        return SCRIPT_CMD_FAILURE;
    }
    let Some(nd) = nd else {
        return SCRIPT_CMD_FAILURE; // ain't possible, but I don't trust people.
    };
    let nd_id = nd.id();
    let mut m = crate::pc_misc::npcm(c, nd_id);
    m.level = npc_level;
    m.stat_point = stat_point;
    crate::pc_misc::put_npcm(c, &m);
    // if (!nd->status.hp) status_calc_npc(nd, SCO_FIRST); else status_calc_npc(nd, SCO_NONE);
    super::script2::unitinfo::npc_status_calc_first_or_none(c, nd_id);
    let Some(sd) = crate::unit::get(c, aid) else { return SCRIPT_CMD_SUCCESS };
    if crate::db::skills::skill_get_inf(skill_id) & crate::db::skills::INF_GROUND_SKILL as i32 != 0 {
        crate::skill::unit::unit_skilluse_pos2(c, nd_id, sd.x, sd.y, skill_id, skill_level, 0, 0, true);
    } else {
        crate::skill::unit::unit_skilluse_id2(c, nd_id, sd.id, skill_id, skill_level, 0, 0, true);
    }
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- spirit spheres

/// The optional player of addspiritball / delspiritball / countspiritball (argument `loc`): a number is a char id
/// (map_charid2sd), a string a name (map_nick2sd, no partial match) — neither reports a missing player —; without the
/// argument the attached player (script_rid2sd: an error and the end of the script when there is none).
fn spiritball_sd(rt: &Rt, st: &mut State, loc: i32, func: &str) -> Option<u32> {
    if st.hasdata(loc) {
        if !isstring(rt, st, loc) {
            let id = getnum(rt, st, loc);
            rt.c.and_then(|c| crate::pc::charid2sd(c, id as u32)).map(|u| u.id)
        } else {
            let name = lat(&getstr(rt, st, loc));
            rt.c.and_then(|c| crate::pc::nick2sd(c, &name, false)).map(|u| u.id)
        }
    } else {
        rid2sd(rt, st, func)
    }
}

/// delspiritball's `uint8 count`, 0 meaning 1.
pub fn delspiritball_count(count: i32) -> u8 {
    match count as u8 {
        0 => 1,
        n => n,
    }
}

/// BUILDIN_FUNC(addspiritball) (script.cpp:24027-24053) [Cydh]: addspiritball <count>,<duration>{,<char id>|"<name>"};
/// — `count` times pc_addspiritball(sd, duration, 10) (each sphere lasts `duration` ms; at 10 the oldest goes). No
/// player: SCRIPT_CMD_FAILURE; a count of 0 (after the uint8 conversion) does nothing.
fn addspiritball(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = spiritball_sd(rt, st, 4, "buildin_addspiritball") else { return SCRIPT_CMD_FAILURE };
    let count = getnum(rt, st, 2) as u8; // uint8 count
    if count == 0 {
        return SCRIPT_CMD_SUCCESS;
    }
    let duration = getnum(rt, st, 3) as u16; // uint16 duration
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    for _ in 0..count {
        crate::skill::state::pc_addspiritball(c, aid, duration as i32, 10);
    }
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(delspiritball) (script.cpp:24061-24082) [Cydh]: delspiritball <count>{,<char id>|"<name>"}; —
/// pc_delspiritball(sd, count, 0) (with the client effect); a count of 0 removes one. No player: SCRIPT_CMD_FAILURE.
fn delspiritball(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = spiritball_sd(rt, st, 3, "buildin_delspiritball") else { return SCRIPT_CMD_FAILURE };
    let count = delspiritball_count(getnum(rt, st, 2));
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    crate::skill::state::pc_delspiritball(c, aid, count as i32, 0);
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(countspiritball) (script.cpp:24090-24105) [Cydh]: countspiritball({<char id>|"<name>"}) — the
/// player's spirit spheres (sd->spiritball). No player: nothing pushed, SCRIPT_CMD_FAILURE.
fn countspiritball(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = spiritball_sd(rt, st, 2, "buildin_countspiritball") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    st.pushint(crate::skill::state::get(c, aid).spiritball as i64);
    SCRIPT_CMD_SUCCESS
}
