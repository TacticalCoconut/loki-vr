//! Small helpers of other rAthena files that the status change switches call (pc.hpp macros, clif.hpp's emotion
//! numbers, skill.cpp skill_sit), ported once here for every sc_*.rs file.

use crate::clif;
use crate::db::skills::ids::{RG_GANGSTER, TK_HPTIME, TK_SPTIME};
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// clif.hpp enum emotion_type (the ones the status changes use).
pub const ET_QUESTION: u8 = 1;
pub const ET_THROB: u8 = 3;
pub const ET_SWEAT: u8 = 4;
pub const ET_SMILE: u8 = 18;
pub const ET_CRY: u8 = 28;

/// clif.hpp `#define clif_status_load(bl, type, flag) clif_status_change((bl), (type), (flag), 0, 0, 0, 0)`.
pub fn clif_status_load(c: &Ctx, bl: &Unit, efst: i32, flag: i32) {
    super::clif::status_change(c, bl, efst, flag, 0, 0, 0, 0);
}

/// pc.hpp `pc_issit(sd)`: `(sd)->vd.dead_sit == 2`.
pub fn pc_issit(c: &Ctx, aid: u32) -> bool {
    unit::get(c, aid).map(|u| u.vd.dead_sit == 2).unwrap_or(false)
}

/// pc.hpp `pc_setsit(sd)`: `unit_stop_walking(sd, USW_FIXPOS|USW_MOVE_FULL_CELL); unit_stop_attack(sd);
/// sd->state.dead_sit = sd->vd.dead_sit = 2`.
pub fn pc_setsit(c: &Ctx, aid: u32) {
    let Some(mut u) = unit::get(c, aid) else { return };
    unit::stop_walking(c, &mut u, unit::USW_FIXPOS | unit::USW_MOVE_FULL_CELL, 0);
    unit::save(c, &u);
    let Some(mut u) = unit::get(c, aid) else { return };
    unit::stop_attack(c, &mut u);
    u.vd.dead_sit = 2;
    unit::save(c, &u);
    if let Some(mut p) = crate::pc::get(c, aid) {
        p.dead_sit = 2;
        crate::pc::put(c, &p);
    }
}

/// pc.hpp `pc_check_weapontype(sd, type)`: `type & (status.weapon < MAX_WEAPON_TYPE ? 1 << status.weapon :
/// (1 << weapontype1) | (1 << weapontype2) | (1 << status.weapon))`.
pub fn pc_check_weapontype(c: &Ctx, aid: u32, type_: i32) -> bool {
    let weapon = crate::pc::character_of(c, aid).map(|ch| ch.weapon as i32).unwrap_or(0);
    let mask: i64 = if weapon < super::defs::MAX_WEAPON_TYPE {
        1i64 << weapon
    } else {
        let (w1, w2) = crate::inventory::st(c, aid).map(|s| (s.weapontype1 as i64, s.weapontype2 as i64)).unwrap_or((0, 0));
        (1i64 << w1) | (1i64 << w2) | (1i64 << weapon)
    };
    (type_ as i64) & mask != 0
}

/// `pc_checkskill(sd, skill_id)`.
pub fn pc_checkskill(c: &Ctx, aid: u32, skill_id: u16) -> i32 {
    crate::skill::checkskill(c, aid, skill_id) as i32
}

/// skill.cpp skill_sit_count (map_foreachinallrange's callback: counts the sitting players with the skills).
fn skill_sit_count(c: &Ctx, sd: &Unit, flag: i32) -> i32 {
    if sd.vd.dead_sit != 2 {
        return 0;
    }
    if flag & 1 != 0 && pc_checkskill(c, sd.id, RG_GANGSTER) > 0 {
        return 1;
    }
    if flag & 2 != 0 && (pc_checkskill(c, sd.id, TK_HPTIME) > 0 || pc_checkskill(c, sd.id, TK_SPTIME) > 0) {
        return 1;
    }
    0
}

fn count_sitting(c: &Ctx, center: &Unit, range: i32, flag: i32) -> i32 {
    crate::map::units_in_range(c, center.map, center.x, center.y, range as i16, crate::map::BL_PC).iter().map(|u| skill_sit_count(c, u, flag)).sum()
}

/// skill.cpp skill_sit_in: a sitting player with the skills gets its bonus state.
fn skill_sit_in(c: &Ctx, sd: &Unit, flag: i32) {
    if sd.vd.dead_sit != 2 {
        return;
    }
    if flag & 1 != 0 && pc_checkskill(c, sd.id, RG_GANGSTER) > 0 {
        super::data::with_sd(c, sd.id, |s| s.gangsterparadise = true);
    }
    if flag & 2 != 0 && (pc_checkskill(c, sd.id, TK_HPTIME) > 0 || pc_checkskill(c, sd.id, TK_SPTIME) > 0) {
        super::data::with_sd(c, sd.id, |s| s.rest = true);
        super::calc::recalc_regen(c, sd.id);
    }
}

/// skill.cpp skill_sit_out: the bonus state ends when fewer than two players with the skills sit around.
fn skill_sit_out(c: &Ctx, sd: &Unit, flag: i32, range: i32) {
    if count_sitting(c, sd, range, flag) > 1 {
        return;
    }
    let Some(s) = super::data::sd_get(c, sd.id) else { return };
    if flag & 1 != 0 && s.gangsterparadise {
        super::data::with_sd(c, sd.id, |s| s.gangsterparadise = false);
    }
    if flag & 2 != 0 && s.rest {
        super::data::with_sd(c, sd.id, |s| s.rest = false);
        super::calc::recalc_regen(c, sd.id);
    }
}

/// skill.cpp skill_sit: the sitting icon, and Gangster's Paradise / Taekwon rest states of the players around.
pub fn skill_sit(c: &Ctx, aid: u32, sitting: bool) -> i32 {
    use crate::db::skills::skill_get_splash;
    let Some(sd) = unit::get(c, aid) else { return 0 };
    let mut flag = 0;
    let mut range = 0;
    let lv = pc_checkskill(c, aid, RG_GANGSTER);
    if lv > 0 {
        flag |= 1;
        range = skill_get_splash(RG_GANGSTER, lv as u16);
    }
    let lv = pc_checkskill(c, aid, TK_HPTIME);
    if lv > 0 {
        flag |= 2;
        range = skill_get_splash(TK_HPTIME, lv as u16);
    } else {
        let lv = pc_checkskill(c, aid, TK_SPTIME);
        if lv > 0 {
            flag |= 2;
            range = skill_get_splash(TK_SPTIME, lv as u16);
        }
    }
    clif_status_load(c, &sd, super::consts::EFST_SIT, sitting as i32);
    if flag == 0 {
        return 0; // No need to count area if no skills are learned.
    }
    let around = crate::map::units_in_range(c, sd.map, sd.x, sd.y, range as i16, crate::map::BL_PC);
    if sitting {
        if count_sitting(c, &sd, range, flag) > 1 {
            for u in &around {
                if let Some(u) = unit::get(c, u.id) {
                    skill_sit_in(c, &u, flag);
                }
            }
        }
    } else {
        for u in &around {
            if let Some(u) = unit::get(c, u.id) {
                skill_sit_out(c, &u, flag, range);
            }
        }
    }
    0
}

/// `pc_setsit(sd); skill_sit(sd, true); clif_sitting(*sd);` (the sequence the sit-down statuses run).
pub fn sit_down(c: &Ctx, aid: u32) {
    pc_setsit(c, aid);
    skill_sit(c, aid, true);
    if let Some(u) = unit::get(c, aid) {
        clif::sitting(c, &u);
    }
}

/// skill.cpp skill_check_cloaking / skill_check_camouflage: a wall next to the unit (one of the 8 cells around is
/// CELL_CHKNOPASS).
fn wall_around(c: &Ctx, bl: &Unit) -> bool {
    const DX: [i16; 8] = [0, 1, 0, -1, -1, 1, 1, -1];
    const DY: [i16; 8] = [-1, 0, 1, 0, -1, -1, 1, 1];
    (0..8).any(|i| crate::map::getcell(c, bl.map, bl.x + DX[i], bl.y + DY[i], crate::map::CellChk::ChkNoPass))
}

/// skill.cpp skill_check_cloaking: whether there is a wall around (checked per the cloak check config); with the
/// unit's SC_CLOAKING entry (`with_sce`) it also ends Cloaking without a wall at level < 3 or sets / clears the wall
/// speed bonus (val4 & 1).
pub fn skill_check_cloaking(c: &Ctx, bl: &Unit, with_sce: bool) -> bool {
    use super::consts::SC_CLOAKING;
    use super::sc_conf::{monster_cloak_check_type, pc_cloak_check_type};
    let mut wall = true;
    if (bl.kind == crate::map::BL_PC && pc_cloak_check_type() & 1 != 0) || (bl.kind != crate::map::BL_PC && monster_cloak_check_type() & 1 != 0) {
        //Check for walls.
        if !wall_around(c, bl) {
            wall = false;
        }
    }
    if with_sce {
        if let Some(mut sce) = super::sc::get_sce(c, bl.id, SC_CLOAKING) {
            if !wall {
                if sce.val1 < 3 {
                    //End cloaking.
                    super::sc::status_change_end(c, bl.id, SC_CLOAKING, 0);
                } else if sce.val4 & 1 != 0 {
                    //Remove wall bonus
                    sce.val4 &= !1;
                    super::sc::put_sce(c, &sce);
                    super::calc_bl(c, bl.id, 1u64 << super::consts::SCB_SPEED, super::SCO_NONE);
                }
            } else if sce.val4 & 1 == 0 {
                //Add wall speed bonus
                sce.val4 |= 1;
                super::sc::put_sce(c, &sce);
                super::calc_bl(c, bl.id, 1u64 << super::consts::SCB_SPEED, super::SCO_NONE);
            }
        }
    }
    wall
}

/// skill.cpp skill_check_camouflage: whether a player has a wall around; with the unit's SC_CAMOUFLAGE entry it ends
/// Camouflage without a wall at level < 3, and recalculates the speed.
pub fn skill_check_camouflage(c: &Ctx, bl: &Unit, with_sce: bool) -> bool {
    use super::consts::SC_CAMOUFLAGE;
    let mut wall = true;
    if bl.kind == crate::map::BL_PC && !wall_around(c, bl) {
        //Check for walls.
        wall = false;
    }
    if with_sce {
        if let Some(sce) = super::sc::get_sce(c, bl.id, SC_CAMOUFLAGE) {
            if !wall && sce.val1 < 3 {
                //End camouflage.
                super::sc::status_change_end(c, bl.id, SC_CAMOUFLAGE, 0);
            }
            super::calc_bl(c, bl.id, 1u64 << super::consts::SCB_SPEED, super::SCO_NONE);
        }
    }
    wall
}

/// clif.hpp e_bossmap_info.
pub const BOSS_INFO_NOT: u8 = 0;
pub const BOSS_INFO_ALIVE: u8 = 1;
pub const BOSS_INFO_ALIVE_WITHMSG: u8 = 2;
pub const BOSS_INFO_DEAD: u8 = 3;

/// map.cpp bossid_db membership. map_addiddb puts a monster there when `md->state.boss` is set (map.cpp:2183), and it
/// runs once, in mob_spawn_dataset (mob.cpp:496), right after `md->state.boss = data->state.boss` (mob.cpp:475): the
/// flag of the spawn_data the monster is made from, which only a `boss_monster` spawn line sets (npc.cpp:5228), and
/// those monsters keep that spawn_data as `md->spawn` (npc_parse_mob2). mob_spawn then clears the whole `md->state`
/// (mob.cpp:1171 memset; `MobData::boss` is that bit and is false from the first spawn on), but the monster STAYS in
/// bossid_db until map_deliddb (map.cpp:2209, unit_free). So a monster is in bossid_db exactly when its spawn data has
/// the boss flag. (Wave H, hplay2: this read `md.boss`, which mob_spawn has always cleared, so the Convex Mirror never
/// found a boss on the module: the w4social client smoke, the status2 scenario bossinfo.)
fn in_bossid_db(c: &Ctx, md: &crate::mob::MobData) -> bool {
    use crate::npc::spawn_data;
    md.spawn != 0 && c.db().spawn_data().id().find(md.spawn).map(|sp| sp.boss).unwrap_or(false)
}

/// map.cpp map_getmob_boss: the first monster of bossid_db (the `boss_monster` spawns, added by map_addiddb and
/// removed by map_deliddb, so dead ones waiting for their respawn stay) on map m, in the int DBMap's iteration order.
pub fn map_getmob_boss(c: &Ctx, m: u16) -> Option<crate::mob::MobData> {
    use crate::unit::unit as _;
    let mut bosses: Vec<crate::mob::MobData> = c
        .db()
        .unit()
        .by_map_kind()
        .filter((m, crate::map::BL_MOB))
        .filter_map(|u| crate::mob::get(c, u.id))
        .filter(|md| in_bossid_db(c, md))
        .collect();
    bosses.sort_by_key(|md| crate::party::idb_order(md.id));
    bosses.into_iter().next()
}

/// map.cpp map_id2boss.
pub fn map_id2boss(c: &Ctx, id: i32) -> Option<crate::mob::MobData> {
    if id <= 0 {
        return None;
    }
    crate::mob::get(c, id as u32).filter(|md| in_bossid_db(c, md))
}

/// clif.cpp clif_bossmapinfo: ZC_BOSS_INFO to the player.
pub fn clif_bossmapinfo(c: &Ctx, sd: &Unit, md: Option<&crate::mob::MobData>, flag: u8) {
    use crate::net::packets::PACKET_ZC_BOSS_INFO;
    let mut p = PACKET_ZC_BOSS_INFO { packetType: PACKET_ZC_BOSS_INFO::ID as i16, r#type: flag, ..Default::default() };
    match flag {
        BOSS_INFO_NOT => {} // No data required
        BOSS_INFO_ALIVE_WITHMSG | BOSS_INFO_ALIVE => {
            if let Some(u) = md.and_then(|md| unit::get(c, md.id)) {
                p.x = u.x as u32;
                p.y = u.y as u32;
            }
        }
        BOSS_INFO_DEAD => {
            let due = md.and_then(|md| crate::timer::due(c, md.spawn_timer)).unwrap_or(c.tick());
            let mut seconds = ((due - c.tick()) / 1000 + 60) as u32;
            let hours = (seconds / (60 * 60)) as i32;
            seconds -= 60 * 60 * hours as u32;
            let minutes = (seconds / 60) as i32;
            p.minHours = hours as u16;
            p.minMinutes = minutes as u16;
        }
        _ => {}
    }
    if let Some(md) = md {
        if let Some(db) = crate::mob::db(c, md) {
            p.name = db.jname.clone();
        }
    }
    clif::send(c, &p.to_vec(), sd, clif::Target::SelfOnly);
}

/// status.cpp status_get_sc: the unit types that have a status_change (players, monsters, NPCs, homunculi,
/// mercenaries, elementals).
pub fn has_status_change(kind: u16) -> bool {
    use crate::map::*;
    matches!(kind, BL_PC | BL_MOB | BL_NPC | BL_HOM | BL_MER | BL_ELEM)
}

/// skill.cpp skill_banding_count: the party members (not idle) around within Banding's splash range.
pub fn skill_banding_count(c: &Ctx, aid: u32) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return 0 };
    let range = crate::db::skills::skill_get_splash(crate::db::skills::ids::LG_BANDING, 1) as i16;
    let total: i32 = crate::party::party_foreachsamemap(c, &sd, range).iter().map(|u| crate::party::party_sub_count(c, u.id)).sum();
    super::defs::cap_value(total, 0, crate::party::MAX_PARTY as i32)
}

/// path.hpp distance_xy: `distance(x0 - x1, y0 - y1)`.
pub fn distance_xy(x0: i16, y0: i16, x1: i16, y1: i16) -> i32 {
    crate::path::distance(x0 as i32 - x1 as i32, y0 as i32 - y1 as i32)
}

/// clif.cpp clif_crimson_marker: ZC_C_MARKERINFO, the mark on `bl` shown on the caster's minimap (-1,-1 removes it).
pub fn clif_crimson_marker(c: &Ctx, sd: &Unit, bl: &Unit, remove: bool) {
    use crate::net::packets::PACKET_ZC_C_MARKERINFO;
    let (x, y) = if remove { (-1, -1) } else { (bl.x, bl.y) };
    let p = PACKET_ZC_C_MARKERINFO { PacketType: PACKET_ZC_C_MARKERINFO::ID as i16, AID: bl.id, xPos: x, yPos: y };
    clif::send(c, &p.to_vec(), sd, clif::Target::SelfOnly);
}

/// clif.cpp clif_bladestop: ZC_BLADESTOP to the area of `src`.
pub fn clif_bladestop(c: &Ctx, src: &Unit, target_id: u32, active: bool) {
    use crate::net::packets::PACKET_ZC_BLADESTOP;
    let p = PACKET_ZC_BLADESTOP { packetType: PACKET_ZC_BLADESTOP::ID as i16, srcId: src.id, targetId: target_id, flag: active as u32 };
    clif::send(c, &p.to_vec(), src, clif::Target::Area);
}

/// clif.cpp clif_millenniumshield: ZC_MILLENNIUMSHIELD (the shield count) to the area.
pub fn clif_millenniumshield(c: &Ctx, bl: &Unit, shields: i16) {
    // one copy: the skill pipeline's (skillcore, skill/state.rs)
    crate::skill::state::clif_millenniumshield(c, bl, shields);
}

/// skill.cpp skill_get_range2: a skill's range for this unit (Vulture's Eye, Snake Eye, Shadow Jump, Radius and Research
/// Trap extend it). Ported here for the status changes that need it (SC_CLOSECONFINE's release area); the skill
/// pipeline (skillcore agent) uses the same function.
pub fn skill_get_range2(c: &Ctx, bl: &Unit, skill_id: u16, skill_lv: u16, is_server: bool) -> i32 {
    // one copy: the skill pipeline's (skillcore, skill/misc.rs)
    crate::skill::misc::skill_get_range2(c, bl, skill_id, skill_lv, is_server)
}

/// clif.cpp clif_devotion: ZC_DEVOTIONLIST (0x1cf), the devotion links of `src` (a player's `sd->devotion[]`, 5 links
/// shown; a mercenary's master: phase 7) and the Devotion range; to `tsd` or to the area.
pub fn clif_devotion(c: &Ctx, src: &Unit, tsd: Option<&Unit>) {
    // one copy: the skill pipeline's (skillcore, skill/clif.rs)
    crate::skill::clif::clif_devotion(c, src, tsd);
}

/// `bl->m == sd->feel_map[0].m || … [1].m || … [2].m`: the map is one of the Star Gladiator's feel maps
/// (sd->feel_map: crate::pc_taekwon, the one owner of the feel / hate fields).
pub fn on_feel_map(c: &Ctx, aid: u32, m: i32) -> bool {
    (0..crate::pc_taekwon::MAX_PC_FEELHATE).any(|i| crate::pc_taekwon::feel_map(c, aid, i) == m)
}

/// map.cpp map_moveblock, the status part before a character moves (after `skill_unit_move(bl, tick, 2)`): the
/// holds and stances that a move breaks, and a Fire / Electric Walk that has placed all its units.
pub fn moveblock_before(c: &Ctx, bl_id: u32) {
    use super::consts::*;
    if super::sc::load(c, bl_id).empty() {
        return;
    }
    // at least one to cancel
    super::sc::status_change_end(c, bl_id, SC_CLOSECONFINE, 0);
    super::sc::status_change_end(c, bl_id, SC_CLOSECONFINE2, 0);
    super::sc::status_change_end(c, bl_id, SC_TINDER_BREAKER, 0);
    super::sc::status_change_end(c, bl_id, SC_TINDER_BREAKER2, 0);
    // status_change_end(bl, SC_BLADESTOP); //Won't stop when you are knocked away, go figure...
    super::sc::status_change_end(c, bl_id, SC_TATAMIGAESHI, 0);
    super::sc::status_change_end(c, bl_id, SC_MAGICROD, 0);
    super::sc::status_change_end(c, bl_id, SC_SU_STOOP, 0);
    if let Some(e) = super::sc::get_sce(c, bl_id, SC_PROPERTYWALK) {
        if e.val3 >= crate::db::skills::skill_get_maxcount(e.val1 as u16, e.val2 as u16) {
            super::sc::status_change_end(c, bl_id, SC_PROPERTYWALK, 0);
        }
    }
}

/// map.cpp map_moveblock after the move (after `skill_unit_move(bl, tick, 3)`): a Shadow Form target that went too far
/// from its caster (or whose caster is gone) ends the caster's Shadow Form and forgets it.
pub fn moveblock_shadowform_target(c: &Ctx, bl: &Unit) {
    use crate::map::BL_PC;
    if bl.kind != BL_PC {
        return;
    }
    let id = crate::skill::state::get(c, bl.id).shadowform_id;
    if id == 0 {
        return;
    }
    //Shadow Form Target Moving
    let d_bl = unit::get(c, id);
    let near = d_bl.as_ref().map(|d| crate::path::check_distance((bl.x - d.x) as i32, (bl.y - d.y) as i32, 10)).unwrap_or(false);
    if !near {
        if let Some(d) = d_bl {
            super::sc::status_change_end(c, d.id, super::consts::SC__SHADOWFORM, 0);
        }
        crate::skill::state::with(c, bl.id, |s| s.shadowform_id = 0);
    }
}

/// map.cpp map_moveblock after the move, a character without SC_DANCING: Cloaking (below level 3) ends away from a
/// wall. (Runs before the unit groups that follow the character move.)
pub fn moveblock_cloaking(c: &Ctx, bl: &Unit) {
    use super::consts::SC_CLOAKING;
    if super::sc::get_sce(c, bl.id, SC_CLOAKING).map(|e| e.val1 < 3).unwrap_or(false) && !skill_check_cloaking(c, bl, false) {
        super::sc::status_change_end(c, bl.id, SC_CLOAKING, 0);
    }
}

/// map.cpp map_moveblock after the move, a character without SC_DANCING, after the unit groups that follow it moved:
/// the Shadow Form caster too far from its target, and Fire / Electric Walk leaving a unit on the cell it came from
/// (x0, y0).
pub fn moveblock_after(c: &Ctx, bl: &Unit, x0: i16, y0: i16) {
    use super::consts::{SC_PROPERTYWALK, SC__SHADOWFORM};
    use crate::db::skills::ids::{NPC_ELECTRICWALK, NPC_FIREWALK, SO_ELECTRICWALK, SO_FIREWALK};
    if let Some(e) = super::sc::get_sce(c, bl.id, SC__SHADOWFORM) {
        //Shadow Form Caster Moving
        let near = unit::get(c, e.val2 as u32).map(|d| crate::path::check_distance((bl.x - d.x) as i32, (bl.y - d.y) as i32, 10)).unwrap_or(false);
        if !near {
            super::sc::status_change_end(c, bl.id, SC__SHADOWFORM, 0);
        }
    }
    let Some(e) = super::sc::get_sce(c, bl.id, SC_PROPERTYWALK) else { return };
    let Some(bl) = unit::get(c, bl.id) else { return };
    use crate::skillunit::map_find_skill_unit_oncell as oncell;
    if e.val3 < crate::db::skills::skill_get_maxcount(e.val1 as u16, e.val2 as u16)
        && oncell(c, &bl, bl.x, bl.y, SO_ELECTRICWALK, None, 0).is_none()
        && oncell(c, &bl, bl.x, bl.y, NPC_ELECTRICWALK, None, 0).is_none()
        && oncell(c, &bl, bl.x, bl.y, SO_FIREWALK, None, 0).is_none()
        && oncell(c, &bl, bl.x, bl.y, NPC_FIREWALK, None, 0).is_none()
        && crate::skillunit::skill_unitsetting(c, &bl, e.val1 as u16, e.val2 as u16, x0, y0, 0).is_some()
    {
        if let Some(mut e) = super::sc::get_sce(c, bl.id, SC_PROPERTYWALK) {
            e.val3 += 1;
            super::sc::put_sce(c, &e);
        }
    }
}

/// clif.cpp clif_feel_hate_reset: `clif_starskill(sd, "", 0, 0, 30)` (0x20e: map name, monster id, star, result) to
/// the player.
pub fn clif_feel_hate_reset(c: &Ctx, aid: u32) {
    let Some(sd) = unit::get(c, aid) else { return };
    crate::pc_taekwon::clif_feel_hate_reset(c, &sd);
}

/// The player's `sc.option` (OPTION_* bits), read now.
pub fn option_of(c: &Ctx, aid: u32) -> i32 {
    unit::get(c, aid).map(|u| u.sc.option).unwrap_or(0)
}

/// pc.cpp pc_setriding: mount (flag set, with Knight's Riding) or dismount the Peco Peco; never while SC_ALL_RIDING.
pub fn pc_setriding(c: &Ctx, aid: u32, flag: i32) {
    use super::defs::OPTION_RIDING;
    if super::sc::has_sce(c, aid, super::consts::SC_ALL_RIDING) {
        return;
    }
    if flag != 0 {
        if pc_checkskill(c, aid, crate::db::skills::ids::KN_RIDING) > 0 {
            // add peco
            crate::skilltree::pc_setoption(c, aid, option_of(c, aid) | OPTION_RIDING, 0);
        }
    } else if option_of(c, aid) & OPTION_RIDING != 0 {
        crate::skilltree::pc_setoption(c, aid, option_of(c, aid) & !OPTION_RIDING, 0);
    }
}

/// unit.cpp unit_run_hit: a runner that cannot go on bounces back (SC_RUN) or bites (SC_WUGDASH), ending the status.
fn unit_run_hit(c: &Ctx, bl_id: u32, is_pc: bool, t: i32) {
    use super::consts::{EFST_TING, SC_RUN};
    let lv = super::sc::get_sce(c, bl_id, t).map(|e| e.val1).unwrap_or(0);
    // If you can't run forward, you must be next to a wall, so bounce back. [Skotlex]
    if t == SC_RUN {
        if let Some(u) = unit::get(c, bl_id) {
            super::clif::status_change(c, &u, EFST_TING, 1, 0, 0, 0, 0);
        }
    }
    // Set running to 0 beforehand so status_change_end knows not to enable spurt [Kevin]
    if let Some(mut u) = unit::get(c, bl_id) {
        u.ud.running = false;
        unit::save(c, &u);
    }
    super::sc::status_change_end(c, bl_id, t, 0);
    if t == SC_RUN {
        if let Some(u) = unit::get(c, bl_id) {
            let dir = unit::getdir(&u) as i8;
            let count = crate::db::skills::skill_get_blewcount(crate::db::skills::ids::TK_RUN, lv as u16);
            crate::skill::attack::skill_blown(c, &u, &u, count, dir, crate::skill::attack::BLOWN_NONE);
        }
        if let Some(u) = unit::get(c, bl_id) {
            super::clif::status_change(c, &u, EFST_TING, 0, 0, 0, 0, 0);
        }
    } else if is_pc {
        if let Some(u) = unit::get(c, bl_id) {
            clif::fixpos(c, &u);
            crate::skill::castend::skill_castend_damage_id(c, &u, &u, crate::db::skills::ids::RA_WUGDASH, lv as u16, c.tick(), crate::skill::SD_LEVEL);
        }
    }
}

/// unit.cpp unit_run: the unit runs in the direction stored in the status's val2 until something blocks it. `sd`: the
/// player for SC_WUGDASH (rAthena passes nullptr for SC_RUN).
pub fn unit_run(c: &Ctx, bl_id: u32, sd: bool, t: i32) -> bool {
    let Some(sce) = super::sc::get_sce(c, bl_id, t) else { return false };
    let Some(bl) = unit::get(c, bl_id) else { return false };
    if !unit::can_move(c, &bl) {
        super::sc::status_change_end(c, bl_id, t, 0);
        return false;
    }
    let d = (sce.val2 as usize) & 7;
    let (dir_x, dir_y) = (crate::path::DIRX[d], crate::path::DIRY[d]);
    // Determine destination cell
    let (mut to_x, mut to_y) = (bl.x, bl.y);
    // Search for available path
    let mut i: i32 = 0;
    while i < crate::conf::area_size() as i32 {
        if !crate::map::getcell(c, bl.map, to_x + dir_x, to_y + dir_y, crate::map::CellChk::ChkPass) {
            break;
        }
        // If sprinting and there's a PC/Mob/NPC, block the path [Kevin]
        if crate::map::count_oncell(c, bl.map, to_x + dir_x, to_y + dir_y, crate::map::BL_PC | crate::map::BL_MOB | crate::map::BL_NPC, 0) != 0 {
            break;
        }
        to_x += dir_x;
        to_y += dir_y;
        i += 1;
    }
    // Can't run forward.
    if (to_x == bl.x && to_y == bl.y) || (to_x == bl.x + 1 || to_y == bl.y + 1) || (to_x == bl.x - 1 || to_y == bl.y - 1) {
        unit_run_hit(c, bl_id, sd, t);
        return false;
    }
    if unit::walktoxy(c, bl_id, to_x, to_y, 1) {
        return true;
    }
    // There must be an obstacle nearby. Attempt walking one cell at a time.
    loop {
        to_x -= dir_x;
        to_y -= dir_y;
        i -= 1;
        if !(i > 0 && !unit::walktoxy(c, bl_id, to_x, to_y, 1)) {
            break;
        }
    }
    if i == 0 {
        unit_run_hit(c, bl_id, sd, t);
        return false;
    }
    true
}

/// clif.cpp clif_changeoption2: ZC_NPC_SHOWEFST_UPDATE (the option and opt3 of a non-player) to the area.
pub fn clif_changeoption2(c: &Ctx, bl: &Unit) {
    use crate::net::packets::PACKET_ZC_NPC_SHOWEFST_UPDATE;
    if !has_status_change(bl.kind) {
        return; //How can an option change if there's no sc?
    }
    let mut p = PACKET_ZC_NPC_SHOWEFST_UPDATE {
        packetType: PACKET_ZC_NPC_SHOWEFST_UPDATE::ID as i16,
        gid: bl.id,
        effectState: bl.sc.option as u32,
        level: clif::setlevel(c, bl) as i32,
        showEFST: bl.sc.opt3 as u32,
    };
    if crate::atcmd_disguise::disguised(c, bl) {
        clif::send(c, &p.to_vec(), bl, clif::Target::AreaWos);
        p.gid = crate::atcmd_disguise::disguised_bl_id(bl.id);
        clif::send(c, &p.to_vec(), bl, clif::Target::SelfOnly);
        p.gid = bl.id;
        p.effectState = crate::unit::OPTION_INVISIBLE as u32;
        clif::send(c, &p.to_vec(), bl, clif::Target::SelfOnly);
    } else {
        clif::send(c, &p.to_vec(), bl, clif::Target::Area);
    }
}
