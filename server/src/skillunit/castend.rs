//! skill.cpp's cast-end side of ground skills: skill_castend_pos2 (the shell: the per-skill effects are the SkillImpl
//! classes' castendPos2), skill_castend_map (the Warp Portal / Teleport destination menus) with clif.cpp
//! clif_parse_UseSkillMap, skill_pos_maxcount_check, skill_check_unit_range(_sub) and skill_check_unit_range2(_sub)
//! (with npc.cpp npc_isnear_sub), the songs of Renewal (skill_castend_song, skill_apply_songs), the ensemble / chorus
//! partner search (skill_check_pc_partner, skill_check_condition_char_sub) and skill_usave_add / skill_usave_trigger.

use spacetimedb::{table, Table};

use crate::battle::target::battle_check_target;
use crate::battle::{BCT_ENEMY, BCT_GUILD, BCT_PARTY, BCT_WOS, BF_LONG, BF_MAGIC, BF_MISC, BF_SKILL};
use crate::db::skills::{self as sk, *};
use crate::map::{BL_CHAR, BL_MOB, BL_NPC, BL_PC, BL_SKILL};
use crate::status::consts::*;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

use super::consts::*;
use super::data::{self};
use super::{conf, group, iface, setting, util};

/// skill.cpp skill_castend_pos2: the ground skill's cast ends at (x, y).
#[allow(clippy::too_many_arguments)]
pub fn skill_castend_pos2(c: &Ctx, src: &Unit, x: i16, y: i16, skill_id: u16, skill_lv: u16, tick: Tick, flag: i32) -> i32 {
    let mut flag = flag;
    if skill_id > 0 && skill_lv == 0 {
        return 0; // celest
    }
    if sc::status_isdead(c, src) {
        return 0;
    }
    let sd = src.kind == BL_PC;
    match skill_id {
        //Effect is displayed on respective switch case.
        WZ_METEOR | WZ_ICEWALL | MO_BODYRELOCATION | CR_CULTIVATION | HW_GANBANTEIN | sk::SC_ESCAPE | SU_CN_METEOR | NPC_RAINOFMETEOR
        | HN_METEOR_STORM_BUSTER | NW_GRENADES_DROPPING => {}
        _ => {
            if skill_get_inf(skill_id) & INF_SELF_SKILL as i32 != 0 {
                crate::battle::atcommand::clif_skill_nodamage(c, Some(src), src, skill_id, skill_lv as i32, true);
            } else {
                crate::mob::clif::skill_poseffect(c, src, skill_id, skill_lv, x, y, tick);
            }
        }
    }
    let has_impl = skill_db().find(skill_id).is_some() && iface::impl_castend_pos2(c, src, x, y, skill_id, skill_lv, tick, &mut flag).is_some();
    if !has_impl {
        log::warn!("skill_castend_pos2: Unknown skill used:{skill_id}");
        return 1;
    }
    if sc::has_sce(c, src.id, SC_CURSEDCIRCLE_ATKER) {
        //Should only remove after the skill has been casted.
        sc::status_change_end(c, src.id, SC_CURSEDCIRCLE_ATKER, 0);
    }
    if sd {
        // ensure that the skill last-cast tick is recorded
        iface::set_canskill_tick(c, src.id, c.tick());
        if iface::arrow_atk(c, src.id) && flag & 1 == 0 {
            // consume arrow if this is a ground skill
            crate::battle::weapon_attack::battle_consume_ammo(c, src.id, skill_id, skill_lv);
        }
        iface::skill_onskillusage(c, src.id, None, skill_id, tick);
        // perform skill requirement consumption
        if flag & SKILL_NOCONSUME_REQ == 0 {
            iface::skill_consume_requirement(c, src.id, skill_id, skill_lv, 2);
        }
    }
    0
}

/// `skill_failed(sd)` of skill_castend_map: `sd->menuskill_id = sd->menuskill_val = 0`.
fn skill_failed(c: &Ctx, aid: u32) {
    let (_, _, val2) = crate::itemui::menu(c, aid);
    crate::itemui::set_menu(c, aid, 0, 0, val2);
}

/// skill.cpp skill_castend_map: the destination chosen in a Warp Portal / Teleport menu ("cancel" closes it).
pub fn skill_castend_map(c: &Ctx, aid: u32, skill_id: u16, mapname: &str) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return 0 };
    let (menu_id, menu_val, _) = crate::itemui::menu(c, aid);
    if skill_id as i32 != menu_id {
        return 0;
    }
    if !sd.on_map || crate::pc::isdead(c, aid) {
        skill_failed(c, aid);
        return 0;
    }
    if sc::cant(c, aid, SCS_NOCAST) {
        skill_failed(c, aid);
        return 0;
    }
    if let Some(mut u) = unit::get(c, aid) {
        unit::stop_attack(c, &mut u);
        unit::save(c, &u);
    }
    // (skill_log: off)
    if mapname == "cancel" {
        skill_failed(c, aid);
        return 0;
    }
    match skill_id {
        AL_TELEPORT | ALL_ODINS_RECALL => {
            //The storage window is closed automatically by the client when there's
            //any kind of map change, so we need to restore it automatically
            //bugreport:8027
            if mapname == "Random" {
                crate::script::buildins::pcinfo::pc_randomwarp(c, aid, unit::CLR_TELEPORT, false);
            } else if menu_val > 1 || skill_id == ALL_ODINS_RECALL {
                //Need lv2 to be able to warp here.
                if let Some(ch) = crate::pc::character_of(c, aid) {
                    // mapindex_name2id(save_point.map): an unknown map is index 0 (pc_setpos refuses it)
                    if let Some(m) = crate::data::maps().id_of(&ch.save_map) {
                        crate::pc::setpos(c, aid, m, ch.save_x as i16, ch.save_y as i16, unit::CLR_TELEPORT);
                    }
                }
            }
            crate::storage::clif_refresh_storagewindow(c, aid);
        }
        AL_WARP => {
            // mapindex_name2id: the module's map id stands for rAthena's map index (every map is on this server)
            let Some(mapindex) = crate::data::maps().id_of(mapname) else {
                //Given map not found?
                crate::skill::skill_fail(c, &sd, skill_id, 0);
                skill_failed(c, aid);
                return 0;
            };
            let ch = crate::pc::character_of(c, aid);
            let memo = ch.as_ref().map(|ch| crate::pc_misc::memo_points(c, ch.id)).unwrap_or_default();
            let mut p: Vec<(String, i16, i16)> = Vec::new();
            if let Some(ch) = &ch {
                p.push((ch.save_map.clone(), ch.save_x as i16, ch.save_y as i16));
            }
            for m in &memo {
                p.push((m.map.clone(), m.x, m.y));
            }
            // skill_get_maxcount(skill_id, sd->menuskill_val): the uint16 "level" is the portal cell's y (quirk kept)
            let mut maxcount = skill_get_maxcount(skill_id, menu_val as u16);
            if maxcount > 0 {
                maxcount = group::unit_skillunit_maxcount(c, aid, skill_id, maxcount);
                if maxcount == 0 {
                    crate::skill::skill_fail(c, &sd, skill_id, 0);
                    skill_failed(c, aid);
                    return 0;
                }
            }
            let (item_id, item_lv, _) = iface::skillitem(c, aid);
            let mut lv = if item_id == skill_id { item_lv as i32 } else { crate::skill::checkskill(c, aid, skill_id) as i32 };
            let wx = (menu_val >> 16) as i16;
            let wy = (menu_val & 0xffff) as i16;
            if lv <= 0 {
                return 0;
            }
            if lv > 4 {
                lv = 4; // crash prevention
            }
            // check if the chosen map exists in the memo list (strncmp over MAP_NAME_LENGTH_EXT)
            let found = p.iter().take(lv as usize).find(|(m, _, _)| m.as_str() == mapname);
            let Some(&(_, x, y)) = found else {
                skill_failed(c, aid);
                return 0;
            };
            if !iface::skill_check_condition_castend(c, aid, menu_id as u16, lv as u16) {
                // This checks versus skill_id/skill_lv...
                skill_failed(c, aid);
                return 0;
            }
            iface::skill_consume_requirement(c, aid, menu_id as u16, lv as u16, 2);
            iface::clear_skillitem(c, aid); // Clear data that's skipped in 'skill_castend_pos' [Inkfish]
            let Some(sd) = unit::get(c, aid) else { return 0 };
            let Some(gid) = setting::skill_unitsetting(c, &sd, skill_id, lv as u16, wx, wy, 0) else {
                skill_failed(c, aid);
                return 0;
            };
            data::upd_group(c, gid, |g| {
                g.val1 = (g.val1 << 16) | 0;
                // record the destination coordinates
                g.val2 = ((x as i32) << 16) | y as i32;
                g.val3 = mapindex as i32;
            });
        }
        _ => {}
    }
    let (_, _, val2) = crate::itemui::menu(c, aid);
    crate::itemui::set_menu(c, aid, 0, 0, val2);
    0
}

/// common/mapindex.cpp mapindex_getmapname: ".gat" stripped, at most MAP_NAME_LENGTH - 1 characters.
fn mapindex_getmapname(raw: &str) -> String {
    const MAP_NAME_LENGTH: usize = 12;
    const MAP_NAME_LENGTH_EXT: usize = 16;
    let b = crate::net::wire::latin1(raw);
    let mut len = b.iter().take(MAP_NAME_LENGTH_EXT).take_while(|&&x| x != 0).count();
    if len == MAP_NAME_LENGTH_EXT {
        len -= 1;
    }
    if len >= 4 && b[len - 4..len].eq_ignore_ascii_case(b".gat") {
        len -= 4; // strip .gat extension
    }
    let len = len.min(MAP_NAME_LENGTH - 1);
    b[..len].iter().map(|&x| x as char).collect()
}

/// clif.cpp clif_parse_UseSkillMap: CZ_SELECT_WARPPOINT (0x011b), the answer to a destination menu.
pub fn clif_parse_use_skill_map(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = crate::net::packets::PACKET_CZ_SELECT_WARPPOINT::read(&mut crate::net::wire::Reader::new(pkt)) else { return };
    crate::itemui::set_workinprogress(c, aid, crate::itemui::WIP_DISABLE_NONE);
    let (menu_id, _, _) = crate::itemui::menu(c, aid);
    if p.skill_id as i32 != menu_id {
        return;
    }
    //It is possible to use teleport with the storage window open bugreport:8027
    let Some(pc) = crate::pc::get(c, aid) else { return };
    let opt1 = unit::get(c, aid).map(|u| u.sc.opt1).unwrap_or(0);
    if crate::pc::cant_act(&pc, opt1) && pc.storage_flag == 0 && p.skill_id as u16 != AL_TELEPORT {
        crate::itemui::clif_menuskill_clear(c, aid);
        return;
    }
    crate::pc::delinvincibletimer(c, aid);
    let map_name = mapindex_getmapname(&p.mapname);
    skill_castend_map(c, aid, p.skill_id as u16, &map_name);
}

// ---------------------------------------------------------------------------------------------- placement checks

/// skill.cpp skill_check_unit_range_sub: a unit that blocks placing `skill_id` next to it (1).
fn skill_check_unit_range_sub(c: &Ctx, bl: &Unit, skill_id: u16) -> i32 {
    if !bl.on_map || bl.kind != BL_SKILL {
        return 0;
    }
    let Some(unit) = data::get_unit(c, bl.id) else { return 0 };
    if !unit.alive {
        return 0;
    }
    let Some(g) = data::skill_id2group(c, unit.group_id) else { return 0 };
    let g_skill_id = g.skill_id;
    match skill_id {
        AL_PNEUMA | MH_STEINWAND | MG_SAFETYWALL | sk::SC_MAELSTROM => {
            //Pneuma doesn't work even if just one cell overlaps with Land Protector
            if !(skill_id == AL_PNEUMA && g_skill_id == SA_LANDPROTECTOR)
                && g_skill_id != MH_STEINWAND && g_skill_id != MG_SAFETYWALL && g_skill_id != AL_PNEUMA && g_skill_id != sk::SC_MAELSTROM
            {
                return 0;
            }
        }
        AL_WARP | HT_SKIDTRAP | MA_SKIDTRAP | HT_LANDMINE | MA_LANDMINE | HT_ANKLESNARE | HT_SHOCKWAVE | HT_SANDMAN | MA_SANDMAN
        | HT_FLASHER | HT_FREEZINGTRAP | MA_FREEZINGTRAP | HT_BLASTMINE | HT_CLAYMORETRAP | HT_TALKIEBOX | RA_ELECTRICSHOCKER
        | RA_CLUSTERBOMB | RA_MAGENTATRAP | RA_COBALTTRAP | RA_MAIZETRAP | RA_VERDURETRAP | RA_FIRINGTRAP | RA_ICEBOUNDTRAP
        | sk::SC_DIMENSIONDOOR | sk::SC_BLOODYLUST | NPC_REVERBERATION | GN_THORNS_TRAP | RL_B_TRAP | sk::SC_ESCAPE => {
            // (HP_BASILICA: pre-renewal only)
            //Non stackable on themselves and traps (including venom dust which does not has the trap inf2 set)
            if skill_id != g_skill_id && !skill_get_inf2(g_skill_id, &[INF2_ISTRAP]) && g_skill_id != AS_VENOMDUST && g_skill_id != MH_POISON_MIST {
                return 0;
            }
        }
        _ => {
            //Avoid stacking with same kind of trap. [Skotlex]
            if g_skill_id != skill_id {
                return 0;
            }
        }
    }
    1
}

/// skill.cpp skill_check_unit_range: units of kinds that may not overlap `skill_id`'s layout around (x, y).
pub fn skill_check_unit_range(c: &Ctx, bl: &Unit, x: i16, y: i16, skill_id: u16, skill_lv: u16) -> i32 {
    //Non players do not check for the skill's splash-trigger area.
    let mut range = if bl.kind == BL_PC { skill_get_unit_range(skill_id, skill_lv) } else { 0 };
    let layout_type = skill_get_unit_layout_type(skill_id, skill_lv);
    if layout_type == -1 || layout_type > MAX_SQUARE_LAYOUT {
        log::error!("skill_check_unit_range: unsupported layout type {layout_type} for skill {skill_id}");
        return 0;
    }
    range += layout_type;
    let r = range as i16;
    let ids = util::ids_in_area(c, bl.map, x - r, y - r, x + r, y + r, BL_SKILL, false);
    util::each(c, &ids, |u| skill_check_unit_range_sub(c, u, skill_id))
}

/// skill.cpp skill_check_unit_range2_sub: a character in the way of a no-footset skill.
fn skill_check_unit_range2_sub(c: &Ctx, bl: &Unit, skill_id: u16) -> i32 {
    if !bl.on_map {
        return 0;
    }
    if sc::status_isdead(c, bl) && skill_id != AL_WARP {
        return 0;
    }
    // (HP_BASILICA: pre-renewal only)
    if skill_id == AM_DEMONSTRATION && bl.kind == BL_MOB && crate::mob::get(c, bl.id).map(|m| m.mob_id as u32 == crate::db::mobs::MOBID_EMPERIUM).unwrap_or(false) {
        return 0; //Allow casting Bomb/Demonstration Right under emperium [Skotlex]
    }
    1
}

/// npc.cpp npc_isnear_sub: a visible, non-dynamic NPC; with a skill, one of the NPC types its NoNearNPC names.
pub fn npc_isnear_sub(c: &Ctx, bl: &Unit, skill_id: u16) -> i32 {
    if bl.sc.option & crate::unit::OPTION_HIDE != 0 {
        return 0;
    }
    if bl.npc_owner_char != 0 {
        return 0;
    }
    if skill_id > 0 {
        //If skill_id > 0 that means is used for INF2_DISABLENEARNPC [Cydh]
        let Some(skill) = skill_db().find(skill_id) else { return 0 };
        let t = skill.unit_nonearnpc_type;
        if t != 0 {
            let subtype = c.db().npc().id().find(bl.id).map(|n| n.subtype).unwrap_or(u8::MAX);
            use crate::npc::{NPCTYPE_SCRIPT, NPCTYPE_SHOP, NPCTYPE_TOMB, NPCTYPE_WARP};
            if t & SKILL_NONEAR_WARPPORTAL != 0 && subtype == NPCTYPE_WARP {
                return 1;
            }
            if t & SKILL_NONEAR_SHOP != 0 && subtype == NPCTYPE_SHOP {
                return 1;
            }
            if t & SKILL_NONEAR_NPC != 0 && subtype == NPCTYPE_SCRIPT {
                return 1;
            }
            if t & SKILL_NONEAR_TOMB != 0 && subtype == NPCTYPE_TOMB {
                return 1;
            }
        }
        return 0;
    }
    1
}

/// skill.cpp skill_check_unit_range2: characters (or, with `is_near_npc`, NPCs of the skill's NoNearNPC types) in the
/// way of the skill at (x, y).
pub fn skill_check_unit_range2(c: &Ctx, bl: &Unit, x: i16, y: i16, skill_id: u16, skill_lv: u16, is_near_npc: bool) -> i32 {
    let mut range = 0;
    //Range for INF2_DISABLENEARNPC is using skill splash value [Cydh]
    if is_near_npc {
        range = skill_get_splash(skill_id, skill_lv);
    }
    //While checking INF2_DISABLENEARNPC and the range from splash is 0, get the range from skill_unit range and layout. [Cydh]
    if !is_near_npc || range == 0 {
        match skill_id {
            // to be expanded later
            WZ_ICEWALL => range = 2,
            sk::SC_MANHOLE => range = 0,
            _ => {
                let layout_type = skill_get_unit_layout_type(skill_id, skill_lv);
                if layout_type == -1 || layout_type > MAX_SQUARE_LAYOUT {
                    log::error!("skill_check_unit_range2: unsupported layout type {layout_type} for skill {skill_id}");
                    return 0;
                }
                range = skill_get_unit_range(skill_id, skill_lv) + layout_type;
            }
        }
    }
    let skill_npc_range = skill_db().find(skill_id).map(|s| s.unit_nonearnpc_range).unwrap_or(0);
    //Check the additional range [Cydh]
    if is_near_npc && skill_npc_range > 0 {
        range += skill_npc_range as i32;
    }
    let kind = if !is_near_npc {
        //Doesn't check the NPC range
        //If the caster is a monster/NPC, only check for players. Otherwise just check characters
        if bl.kind & conf::skill_nofootset() != 0 {
            BL_CHAR
        } else if bl.kind == BL_MOB {
            BL_MOB //Monsters can never place traps on top of each other regardless of setting
        } else {
            return 0; //Don't check
        }
    } else {
        BL_NPC
    };
    let r = range as i16;
    let ids = util::ids_in_area(c, bl.map, x - r, y - r, x + r, y + r, kind, false);
    if !is_near_npc {
        // !isNearNPC is used for UF_NOFOOTSET, regardless the NPC position, only check the BL_CHAR or BL_PC
        util::each(c, &ids, |u| skill_check_unit_range2_sub(c, u, skill_id))
    } else {
        // isNearNPC is used to check range from NPC
        util::each(c, &ids, |u| npc_isnear_sub(c, u, skill_id))
    }
}

/// skill.cpp skill_isNotOk_npcRange: an NPC of the skill's NoNearNPC types is too near (pos_x, pos_y) for an
/// INF2_DISABLENEARNPC skill (unit_skilluse_id2 / pos2 call it).
pub fn skill_is_not_ok_npc_range(c: &Ctx, src: &Unit, skill_id: u16, skill_lv: u16, pos_x: i16, pos_y: i16) -> bool {
    if src.kind == BL_PC && crate::pc::has_permission(c, src.id, crate::rules::PC_PERM_SKILL_UNCONDITIONAL) {
        return false;
    }
    let (mut pos_x, mut pos_y) = (pos_x, pos_y);
    //if self skill
    if skill_get_inf(skill_id) & INF_SELF_SKILL as i32 != 0 {
        pos_x = src.x;
        pos_y = src.y;
    }
    if pos_x <= 0 {
        pos_x = src.x;
    }
    if pos_y <= 0 {
        pos_y = src.y;
    }
    skill_check_unit_range2(c, src, pos_x, pos_y, skill_id, skill_lv, true) != 0
}

/// skill_castend_id_check's SKE_STAR_BURST / SKE_STAR_CANNON rule: one of `src`'s unit groups (`ud->skillunits`) of
/// `skill_id` has its unit (`sug->unit`, the group's first unit) within `skill_get_unit_range(sug->skill_id,
/// sug->skill_lv)` of `src`. A source without unit_data has none. (rAthena reads a dead first unit's cell too; its row is
/// gone here, but SKE_TWINKLING_GALAXY, the only skill asked about, has one unit, whose death ends the group.)
pub fn skill_caster_unit_in_range(c: &Ctx, src: &Unit, skill_id: u16) -> bool {
    if !group::has_ud(src.kind) {
        return false;
    }
    for sug in data::groups_of(c, src.id) {
        if sug.skill_id != skill_id {
            continue;
        }
        let Some(su) = data::units_of(c, sug.group_id).into_iter().find(|u| u.idx == 0) else { continue };
        if crate::path::distance((src.x - su.bl.x) as i32, (src.y - su.bl.y) as i32) > skill_get_unit_range(sug.skill_id, sug.skill_lv) {
            continue;
        }
        return true;
    }
    false
}

/// skill_timerskill's WZ_WATERBALL: the next Water Ball cell to consume, from layout slot `start` (skl->type) on:
/// `map_find_skill_unit_oncell(src, x + dx[i], y + dy[i], WZ_WATERBALL, nullptr, 0)` over skill_get_unit_layout.
pub fn skill_waterball_next_unit(c: &Ctx, src: &Unit, skill_lv: u16, x: i16, y: i16, start: i32) -> Option<u32> {
    let dir = if src.x == x && src.y == y { 6 } else { crate::map::calc_dir(src, x, y) };
    let layout = skill_get_unit_layout(WZ_WATERBALL, skill_lv, src.kind == BL_MOB, dir);
    let mut i = start;
    while i >= 0 && i < layout.count {
        let ux = (x as i32 + layout.dx[i as usize]) as i16;
        let uy = (y as i32 + layout.dy[i as usize]) as i16;
        if let Some(unit) = super::movement::map_find_skill_unit_oncell(c, src, ux, uy, WZ_WATERBALL, None, 0) {
            return Some(unit);
        }
        i += 1;
    }
    None
}

/// `map_foreachin(all)area(skill_cell_overlap, m, x0, y0, x1, y1, BL_SKILL, skill_id, &dummy, src)` with `dummy = 1`
/// (skill_timerskill's GN_CRAZYWEED_ATK / RL_FIRE_RAIN): the units the new ones remove. Returns the callbacks' sum.
#[allow(clippy::too_many_arguments)]
pub fn skill_cell_overlap_area(c: &Ctx, src: &Unit, m: u16, x0: i16, y0: i16, x1: i16, y1: i16, skill_id: u16, wall: crate::skill::area::Wall) -> i32 {
    let mut dummy = 1;
    crate::skill::area::foreach_in_area(c, m, x0, y0, x1, y1, BL_SKILL, wall, |c, bl| setting::skill_cell_overlap(c, bl, skill_id, &mut dummy, src))
}

/// skill.cpp skill_pos_maxcount_check: may `src` place `skill_id` at (x, y)? (reiteration, no-footset and the
/// per-caster unit limit; `kind` is the BL_ type the battle_config masks test.)
#[allow(clippy::too_many_arguments)]
pub fn skill_pos_maxcount_check(c: &Ctx, src: &Unit, x: i16, y: i16, skill_id: u16, skill_lv: u16, kind: u16, display_failure: bool) -> bool {
    let sd = crate::pc::get(c, src.id).is_some() && src.kind == BL_PC;
    let Some(skill) = skill_db().find(skill_id) else { return false };
    let fail = |c: &Ctx| {
        if sd && display_failure {
            crate::skill::skill_fail(c, src, skill_id, 0);
        }
        false
    };
    if kind & conf::skill_reiteration() == 0 && skill.has_unit_flag(UF_NOREITERATION) && skill_check_unit_range(c, src, x, y, skill_id, skill_lv) != 0 {
        return fail(c);
    }
    if kind & conf::skill_nofootset() != 0 && skill.has_unit_flag(UF_NOFOOTSET) && skill_check_unit_range2(c, src, x, y, skill_id, skill_lv, false) != 0 {
        return fail(c);
    }
    if kind & conf::land_skill_limit() != 0 {
        let maxcount = skill_get_maxcount(skill_id, skill_lv);
        if maxcount > 0 && group::unit_skillunit_maxcount(c, src.id, skill_id, maxcount) == 0 {
            return fail(c);
        }
    }
    true
}

// ---------------------------------------------------------------------------------------------- songs (RENEWAL)

/// skill.cpp splash_target: `( bl->type == BL_MOB ) ? BL_SKILL|BL_CHAR : BL_CHAR` (players' splashes can't hit ice
/// walls).
fn splash_target(bl: &Unit) -> u16 {
    if bl.kind == BL_MOB {
        BL_SKILL | BL_CHAR
    } else {
        BL_CHAR
    }
}

/// skill.cpp skill_apply_songs: one target of a song.
fn skill_apply_songs(c: &Ctx, target: &Unit, flag: i32, src: &Unit, skill_id: u16, skill_lv: u16, tick: Tick) -> i32 {
    if battle_check_target(c, src, target, flag) > 0 {
        match skill_id {
            // Attack type songs
            BA_DISSONANCE => {
                iface::skill_attack(c, BF_MAGIC, src, src, target, skill_id, skill_lv, tick, 0);
                return 1;
            }
            DC_UGLYDANCE | BD_LULLABY => {
                return iface::skill_additional_effect(c, src, target, skill_id, skill_lv, BF_LONG | BF_SKILL | BF_MISC, crate::battle::ATK_DEF, tick);
            }
            _ => {
                // Buff/Debuff type songs
                if skill_id == CG_HERMODE && src.id != target.id {
                    iface::status_change_clear_buffs(c, target, iface::SCCB_HERMODE); // Should dispell only allies.
                }
                return sc::sc_start(c, Some(src), target, skill_get_sc(skill_id) as i32, 100, skill_lv as i32, skill_get_time(skill_id, skill_lv) as i64) as i32;
            }
        }
    }
    0
}

/// skill.cpp skill_castend_song: a Renewal song / dance / ensemble hits everything in its splash at once. Returns the
/// number of targets.
pub fn skill_castend_song(c: &Ctx, src: &Unit, skill_id: u16, skill_lv: u16, tick: Tick) -> i32 {
    if src.kind != BL_PC {
        log::warn!("skill_castend_song: Expected player type for src!");
        return 0;
    }
    if !skill_get_inf2(skill_id, &[INF2_ISSONG, INF2_ISENSEMBLE]) {
        log::warn!("skill_castend_song: Unknown song skill ID: {skill_id}");
        return 0;
    }
    let mut skill_lv = skill_lv;
    let mut flag = BCT_PARTY;
    match skill_id {
        BD_ROKISWEIL => flag = BCT_ENEMY | BCT_WOS,
        BD_LULLABY | BD_ETERNALCHAOS | BA_DISSONANCE | DC_UGLYDANCE | DC_DONTFORGETME => flag = BCT_ENEMY,
        CG_HERMODE => flag |= BCT_GUILD,
        _ => {}
    }
    crate::battle::atcommand::clif_skill_nodamage(c, Some(src), src, skill_id, skill_lv as i32, true);
    data::set_dance(c, src.id, skill_id, skill_lv);
    if skill_get_inf2(skill_id, &[INF2_ISENSEMBLE]) {
        skill_check_pc_partner(c, src.id, skill_id, &mut skill_lv, 3, 1);
    }
    let ids = util::ids_in_range_wc(c, src, skill_get_splash(skill_id, skill_lv) as i16, splash_target(src));
    util::each(c, &ids, |t| skill_apply_songs(c, t, flag, src, skill_id, skill_lv, tick))
}

// ---------------------------------------------------------------------------------------------- partners

/// skill.cpp skill_check_pc_partner's `static int32 c` and `static int32 p_sd[MAX_PARTY]`: shared by every caller
/// (the search at cast begin, the use at cast end), one row.
#[table(accessor = skill_partner_state)]
#[derive(Clone, Debug)]
pub struct SkillPartnerState {
    #[primary_key]
    pub key: u8,
    pub count: i32,
    pub p_sd: Vec<u32>,
}

fn partner_state(c: &Ctx) -> SkillPartnerState {
    c.db().skill_partner_state().key().find(0).unwrap_or(SkillPartnerState { key: 0, count: 0, p_sd: vec![0; crate::party::MAX_PARTY] })
}

fn put_partner_state(c: &Ctx, s: &SkillPartnerState) {
    if c.db().skill_partner_state().key().find(0).is_some() {
        c.db().skill_partner_state().key().update(s.clone());
    } else {
        c.db().skill_partner_state().insert(s.clone());
    }
}

/// skill.cpp skill_check_condition_char_sub: `tsd` around the caster is (one of) the partner(s) a skill needs.
/// Returns the found partner's skill level (ensembles) or 1 / 0, as the foreach sums it.
fn skill_check_condition_char_sub(c: &Ctx, tsd: &Unit, sd: &Unit, st: &mut SkillPartnerState, skill_id: u16) -> i32 {
    let is_chorus = skill_get_inf2(skill_id, &[INF2_ISCHORUS]);
    if skill_id == PR_BENEDICTIO {
        if st.count >= 2 {
            // Check for two companions for Benedictio. [Skotlex]
            return 0;
        }
    } else if is_chorus {
        if st.count == crate::party::MAX_PARTY as i32 {
            // Check for partners for Chorus; Cap if the entire party is accounted for.
            return 0;
        }
    } else if st.count >= 1 {
        // Check for one companion for all other cases.
        return 0;
    }
    if tsd.id == sd.id {
        return 0;
    }
    if crate::pc::isdead(c, tsd.id) {
        return 0;
    }
    if sc::cant(c, tsd.id, SCS_NOCAST) {
        return 0;
    }
    let (Some(sch), Some(tch)) = (crate::pc::character_of(c, sd.id), crate::pc::character_of(c, tsd.id)) else { return 0 };
    let tclass = crate::inventory::class_mapid(&tch);
    let add = |st: &mut SkillPartnerState| {
        let i = st.count as usize;
        if i < st.p_sd.len() {
            st.p_sd[i] = tsd.id;
        }
        st.count += 1;
    };
    use crate::db::jobs::*;
    if is_chorus {
        if tch.party_id != 0 && sch.party_id != 0 && tch.party_id == sch.party_id && (tclass & MAPID_THIRDMASK) == MAPID_MINSTRELWANDERER {
            add(st);
        }
        return 1;
    }
    match skill_id {
        PR_BENEDICTIO => {
            let dir = crate::map::calc_dir(sd, tsd.x, tsd.y);
            let dir = (crate::unit::getdir(sd) as i32 + dir as i32) % 8; //This adjusts dir to account for the direction the sd is facing.
            let sp = crate::status::status_get_status_data(c, sd).sp;
            if (tclass & MAPID_FIRSTMASK) == MAPID_ACOLYTE && (dir == 2 || dir == 6) //Must be standing to the left/right of Priest.
                && sp >= 10
            {
                add(st);
            }
            1
        }
        AB_ADORAMUS => {
            // Adoramus does not consume Blue Gemstone when there is at least 1 Priest class next to the caster
            if (tclass & MAPID_SECONDMASK) == MAPID_PRIEST {
                add(st);
            }
            1
        }
        TR_GEF_NOCTURN | TR_ROKI_CAPRICCIO | TR_AIN_RHAPSODY | TR_MUSICAL_INTERLUDE | TR_JAWAII_SERENADE | TR_NIPELHEIM_REQUIEM
        | TR_PRON_MARCH => {
            // Does the partner's learned skill level affects anything? [Rytech]
            if sch.sex != tch.sex && (tclass & MAPID_FOURTHMASK) == MAPID_TROUBADOURTROUVERE && sch.party_id != 0 && tch.party_id != 0 && sch.party_id == tch.party_id {
                add(st);
            }
            1
        }
        _ => {
            //Warning: Assuming Ensemble Dance/Songs for code speed. [Skotlex]
            let t = unit::get(c, tsd.id).unwrap_or(tsd.clone());
            if t.vd.dead_sit == 2 || !unit::can_move(c, &t) {
                return 0;
            }
            let lv = crate::skill::checkskill(c, tsd.id, skill_id) as i32;
            let wt = crate::inventory::st(c, tsd.id).map(|s| s.weapontype1).unwrap_or(0);
            if sch.sex != tch.sex
                && (tclass & MAPID_SECONDMASK) == MAPID_BARDDANCER
                && lv > 0
                && (wt == crate::inventory::W_MUSICAL || wt == crate::inventory::W_WHIP)
                && sch.party_id != 0
                && tch.party_id != 0
                && sch.party_id == tch.party_id
                && !sc::has_sce(c, tsd.id, SC_DANCING)
            {
                add(st);
                return lv;
            }
            0
        }
    }
}

/// skill.cpp skill_check_pc_partner: searches (cast_flag 0) or uses (cast_flag 1) the partners of an ensemble,
/// chorus, Benedictio, Adoramus or Great Echo. Returns the number of partners (`skill_lv` becomes the average level
/// of an encore).
pub fn skill_check_pc_partner(c: &Ctx, aid: u32, skill_id: u16, skill_lv: &mut u16, range: i32, cast_flag: i32) -> i32 {
    let Some(sd) = unit::get(c, aid).filter(|u| u.kind == BL_PC) else { return 0 };
    let is_chorus = skill_get_inf2(skill_id, &[INF2_ISCHORUS]);
    if !conf::player_skill_partner_check() || crate::pc::has_permission(c, aid, crate::rules::PC_PERM_SKILL_UNCONDITIONAL) {
        return if is_chorus { crate::party::MAX_PARTY as i32 } else { 99 }; //As if there were infinite partners.
    }
    let mut st = partner_state(c);
    if cast_flag != 0 {
        //Execute the skill on the partners.
        match skill_id {
            PR_BENEDICTIO | WM_GREAT_ECHO => {
                for i in 0..st.count.max(0) as usize {
                    if let Some(&pid) = st.p_sd.get(i) {
                        if unit::get(c, pid).map(|u| u.kind == BL_PC).unwrap_or(false) && crate::pc::get(c, pid).is_some() {
                            let sp = if skill_id == PR_BENEDICTIO { 10 } else { skill_get_sp(skill_id, *skill_lv) / 2 };
                            crate::status::hp::status_charge(c, pid, 0, sp as i64);
                        }
                    }
                }
                return st.count;
            }
            AB_ADORAMUS => {
                if st.count > 0 {
                    if let Some(&pid) = st.p_sd.first() {
                        if crate::pc::get(c, pid).is_some() {
                            let i = 2 * *skill_lv as i64;
                            crate::status::hp::status_charge(c, pid, 0, i);
                        }
                    }
                }
            }
            _ => {
                if is_chorus {
                    //Chorus skills are not to be parsed as ensembles
                } else {
                    if skill_get_inf2(skill_id, &[INF2_ISENSEMBLE]) && st.count > 0 {
                        let pid = st.p_sd[0];
                        if let Some(tsd) = unit::get(c, pid).filter(|u| u.kind == BL_PC && crate::pc::get(c, u.id).is_some()) {
                            // RENEWAL: only CG_MOONLIT links the dancers; the other ensembles give both Ensemble Fatigue
                            if sc::has_sce(c, aid, SC_DANCING) && skill_id == CG_MOONLIT {
                                let mut d = sc::get_sce(c, aid, SC_DANCING).expect("SC_DANCING checked");
                                d.val4 = tsd.id as i32;
                                sc::put_sce(c, &d);
                                sc::sc_start4(c, Some(&sd), &tsd, SC_DANCING, 100, skill_id as i32, d.val2, *skill_lv as i32, sd.id as i32, skill_get_time(skill_id, *skill_lv) as i64 + 1000);
                                crate::battle::atcommand::clif_skill_nodamage(c, Some(&tsd), &sd, skill_id, *skill_lv as i32, true);
                                data::set_dance(c, tsd.id, skill_id, *skill_lv);
                            } else {
                                sc::sc_start(c, Some(&sd), &sd, SC_ENSEMBLEFATIGUE, 100, 1, skill_get_time(CG_SPECIALSINGER, *skill_lv) as i64);
                                sc::sc_start(c, Some(&sd), &tsd, SC_ENSEMBLEFATIGUE, 100, 1, skill_get_time(CG_SPECIALSINGER, *skill_lv) as i64);
                            }
                        }
                    }
                    return st.count;
                }
            }
        }
    }
    //Else: new search for partners.
    st.count = 0;
    st.p_sd = vec![0; crate::party::MAX_PARTY];
    let ids = util::ids_in_allrange(c, &sd, range as i16, BL_PC);
    let i = util::each(c, &ids, |tsd| skill_check_condition_char_sub(c, tsd, &sd, &mut st, skill_id));
    put_partner_state(c, &st);
    if skill_id != PR_BENEDICTIO && skill_id != AB_ADORAMUS && skill_id != WM_GREAT_ECHO && !(TR_GEF_NOCTURN..=TR_PRON_MARCH).contains(&skill_id) {
        //Apply the average lv to encore skills.
        *skill_lv = ((i + *skill_lv as i32) / (st.count + 1)) as u16; //I know c should be one, but this shows how it could be used for the average of n partners.
    }
    st.count
}

// ---------------------------------------------------------------------------------------------- unit save

/// skill.cpp skill_usave_add: the unit to set again for this character after its map loads (a warp that does not
/// change map). Replaces an earlier one.
pub fn skill_usave_add(c: &Ctx, aid: u32, skill_id: u16, skill_lv: u16) {
    let Some(char_id) = crate::pc::get(c, aid).map(|p| p.char_id) else { return };
    c.db().skill_usave().char_id().delete(char_id);
    c.db().skill_usave().insert(data::SkillUsave { char_id, skill_id, skill_lv });
}

/// skill.cpp skill_usave_trigger: at LoadEndAck the saved unit is set again at the player's feet.
pub fn skill_usave_trigger(c: &Ctx, aid: u32) {
    let Some(char_id) = crate::pc::get(c, aid).map(|p| p.char_id) else { return };
    let Some(sus) = c.db().skill_usave().char_id().find(char_id) else { return };
    if let Some(sd) = unit::get(c, aid) {
        if let Some(gid) = setting::skill_unitsetting(c, &sd, sus.skill_id, sus.skill_lv, sd.x, sd.y, 0) {
            if sus.skill_id == NC_NEUTRALBARRIER || sus.skill_id == NC_STEALTHFIELD {
                let t = if sus.skill_id == NC_NEUTRALBARRIER { SC_NEUTRALBARRIER_MASTER } else { SC_STEALTHFIELD_MASTER };
                if let Some(sd) = unit::get(c, aid) {
                    sc::sc_start2(c, Some(&sd), &sd, t, 100, sus.skill_lv as i32, gid, skill_get_time(sus.skill_id, sus.skill_lv) as i64);
                }
            }
        }
    }
    c.db().skill_usave().char_id().delete(char_id);
}

#[allow(unused_imports)]
use {group as _, BL_NPC as _};
use super::data::skill_usave as _;
use crate::npc::npc as _;
