//! skill.cpp skill_timerskill / skill_addtimerskill / skill_cleartimerskill: the delayed parts of skills
//! (ud->skilltimerskill[MAX_SKILLTIMERSKILL], one `skill_timerskill` row per used slot; the timer's data is the slot).

use spacetimedb::{table, Table};

use crate::battle::{BCT_ENEMY, DMG_SINGLE};
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_PC, BL_SKILL};
use crate::status::consts::*;
use crate::status::defs::{BF_MAGIC, BF_WEAPON, OPTION_HIDE};
use crate::status::sc;
use crate::timer::{self, Func};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

use super::area::{self, foreach_in_area, foreach_in_range, Wall};
use super::attack::skill_attack;
use super::castend::skill_castend_damage_id;
use super::{SD_ANIMATION, SD_LEVEL, SD_SPLASH, SKILL_ALTDMG_FLAG};

/// skill.hpp MAX_SKILLTIMERSKILL / TIMERSKILL_INTERVAL.
pub const MAX_SKILLTIMERSKILL: u8 = 50;
pub const TIMERSKILL_INTERVAL: i64 = 150;

/// skill.hpp struct skill_timerskill (ud->skilltimerskill[slot]).
#[table(accessor = skill_timerskill, index(accessor = by_src, btree(columns = [src_id])))]
#[derive(Clone, Debug)]
pub struct SkillTimerskill {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub src_id: u32,
    /// The ud->skilltimerskill[] slot (0..MAX_SKILLTIMERSKILL).
    pub slot: u8,
    pub timer: u64,
    pub target_id: u32,
    pub map: u16,
    pub x: i16,
    pub y: i16,
    pub skill_id: u16,
    pub skill_lv: u16,
    /// a BF_ type (NOTE: some places use this as general-purpose storage...)
    pub type_: i32,
    pub flag: i32,
}

/// skill.cpp skill_addtimerskill: 0 added (or the source is off the map), 1 no free slot.
#[allow(clippy::too_many_arguments)]
pub fn skill_addtimerskill(c: &Ctx, src: &Unit, tick: Tick, target: u32, x: i16, y: i16, skill_id: u16, skill_lv: u16, type_: i32, flag: i32) -> i32 {
    if !src.on_map {
        return 0;
    }
    let used: Vec<u8> = c.db().skill_timerskill().by_src().filter(src.id).map(|r| r.slot).collect();
    let Some(slot) = (0..MAX_SKILLTIMERSKILL).find(|s| !used.contains(s)) else { return 1 };
    let timer = timer::add(c, tick, Func::SkillTimerskill, src.id, slot as i64);
    c.db().skill_timerskill().insert(SkillTimerskill { id: 0, src_id: src.id, slot, timer, target_id: target, map: src.map, x, y, skill_id, skill_lv, type_, flag });
    0
}

/// skill.cpp skill_cleartimerskill: deletes the unit's timerskills (a player keeps its Tetra Vortex hits and the
/// Flash Combo chain).
pub fn skill_cleartimerskill(c: &Ctx, src: &Unit) -> i32 {
    let mut rows: Vec<SkillTimerskill> = c.db().skill_timerskill().by_src().filter(src.id).collect();
    rows.sort_by_key(|r| r.slot);
    for r in rows {
        if matches!(r.skill_id, WL_TETRAVORTEX_FIRE | WL_TETRAVORTEX_WATER | WL_TETRAVORTEX_WIND | WL_TETRAVORTEX_GROUND | SR_DRAGONCOMBO | SR_FALLENEMPIRE | SR_TIGERCANNON)
            && src.kind == BL_PC
        {
            continue;
        }
        timer::delete(c, r.timer);
        c.db().skill_timerskill().id().delete(r.id);
    }
    1
}

/// unit_free: the unit's timerskill rows go with it (their timers die with timer::delete_for_target).
pub fn free_unit(c: &Ctx, id: u32) {
    let ids: Vec<u64> = c.db().skill_timerskill().by_src().filter(id).map(|r| r.id).collect();
    for i in ids {
        c.db().skill_timerskill().id().delete(i);
    }
}

/// skill.cpp skill_timerskill (Func::SkillTimerskill; target = the source, data = the slot).
pub fn skill_timerskill(c: &Ctx, _tid: u64, tick: Tick, id: u32, data: i64) {
    let Some(src) = unit::get(c, id) else { return };
    let Some(skl) = c.db().skill_timerskill().by_src().filter(id).find(|r| r.slot as i64 == data) else { return };
    c.db().skill_timerskill().id().delete(skl.id); // ud->skilltimerskill[data] = nullptr
    run(c, &src, &skl, tick);
    // Free skl now that it is no longer needed.
}

/// skill.cpp skill_frostjoke_scream: Frost Joke / Scream's effect on one character around the cell (the skill's
/// applyAdditionalEffects through skill_additional_effect BF_MISC), on enemies and party members alike ("It has
/// been reported that Scream/Joke works the same regardless of woe-setting"), never on the caster, the dead, or an
/// invisible / Mado Gear player.
pub fn skill_frostjoke_scream(c: &Ctx, bl: &Unit, src: &Unit, skill_id: u16, skill_lv: u16, tick: Tick) -> i32 {
    if skill_lv == 0 {
        return 0;
    }
    if src.id == bl.id || sc::status_isdead(c, bl) {
        return 0;
    }
    if bl.kind == BL_PC && bl.sc.option & (crate::status::defs::OPTION_INVISIBLE | crate::status::defs::OPTION_MADOGEAR) != 0 {
        return 0; //Frost Joke / Scream cannot target invisible or MADO Gear characters [Ind]
    }
    let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
    if crate::battle::target::battle_check_target(c, &s, bl, BCT_ENEMY | crate::battle::BCT_PARTY) > 0 {
        super::effect::skill_additional_effect(c, &s, bl, skill_id, skill_lv, crate::status::defs::BF_MISC, crate::battle::ATK_DEF, tick);
    }
    0
}

fn amotion_of(c: &Ctx, u: &Unit) -> i32 {
    crate::status::status_get_status_data(c, u).amotion as i32
}

fn run(c: &Ctx, src: &Unit, skl: &SkillTimerskill, tick: Tick) {
    if !src.on_map {
        return; // Source not on Map
    }
    if skl.target_id != 0 {
        let mut target = unit::get(c, skl.target_id);
        if matches!(skl.skill_id, RG_INTIMIDATE | NPC_FATALMENACE)
            && target.as_ref().map(|t| !t.on_map || !crate::path::check_distance((src.x - t.x) as i32, (src.y - t.y) as i32, crate::conf::area_size() as i32)).unwrap_or(true)
        {
            target = Some(src.clone()); // Required since it has to warp.
        }
        if skl.skill_id == SR_SKYNETBLOW {
            area::set_temp(1, 0);
            clif_damage_ignore(c, src, tick, skl.skill_id, skl.skill_lv);
            let f = skl.flag | BCT_ENEMY | SD_SPLASH | 1;
            foreach_in_range(c, src, skill_get_splash(skl.skill_id, skl.skill_lv) as i16, BL_CHAR | BL_SKILL, Wall::None, |c, bl| {
                area::skill_area_sub(c, bl, src, skl.skill_id, skl.skill_lv, tick, f, skill_castend_damage_id)
            });
            return;
        }
        let Some(target) = target else { return }; // Target offline?
        if !target.on_map {
            return; // Target not on Map
        }
        if src.map != target.map {
            return; // Different Maps
        }
        if sc::status_isdead(c, src) {
            match skl.skill_id {
                WL_CHAINLIGHTNING_ATK | WL_TETRAVORTEX_FIRE | WL_TETRAVORTEX_WATER | WL_TETRAVORTEX_WIND | WL_TETRAVORTEX_GROUND | SR_DRAGONCOMBO | SR_FALLENEMPIRE
                | SR_TIGERCANNON | NPC_DANCINGBLADE_ATK => {
                    if src.kind != BL_PC {
                        return; // (`continue` in rAthena's do-while(0): the same as break)
                    }
                    // Exceptions
                }
                _ => return, // Caster is Dead
            }
        }
        if sc::status_isdead(c, &target) && skl.skill_id != RG_INTIMIDATE && skl.skill_id != WZ_WATERBALL {
            return;
        }
        match skl.skill_id {
            KN_AUTOCOUNTER => {
                super::clif::clif_skill_nodamage(c, Some(src), &target, skl.skill_id, skl.skill_lv as i32, true);
            }
            RG_INTIMIDATE => {
                if warp(c, src, -1, -1, -1) == 0 {
                    let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                    // map.cpp map_search_freecell leaves *x / *y at the search centre (the caster's cell) when it
                    // finds no free cell
                    let (x, y) = crate::mob::spawn::map_search_freecell(c, Some((s.map, s.x, s.y)), 0, 0, 0, 1, 1, 0, 50).unwrap_or((s.x, s.y));
                    if target.id != src.id && !sc::status_isdead(c, &target) {
                        warp(c, &target, -1, x, y);
                    }
                }
            }
            BA_FROSTJOKER | DC_SCREAM => {
                let range = skill_get_splash(skl.skill_id, skl.skill_lv) as i16;
                foreach_in_area(c, skl.map, skl.x - range, skl.y - range, skl.x + range, skl.y + range, BL_CHAR, Wall::None, |c, bl| {
                    skill_frostjoke_scream(c, bl, src, skl.skill_id, skl.skill_lv, tick)
                });
            }
            PR_LEXDIVINA => {
                if src.kind == crate::map::BL_MOB {
                    // Monsters use the default duration when casting Lex Divina
                    let sk = crate::db::misc::status::status_db().get_skill(SC_SILENCE);
                    sc::sc_start(c, Some(src), &target, SC_SILENCE, skl.type_, skl.skill_lv as i32, skill_get_time2(sk, 1) as i64);
                } else {
                    sc::sc_start(c, Some(src), &target, SC_SILENCE, skl.type_, skl.skill_lv as i32, skill_get_time2(skl.skill_id, skl.skill_lv) as i64);
                }
            }
            PR_STRECOVERY => {
                sc::sc_start(c, Some(src), &target, SC_BLIND, skl.type_, skl.skill_lv as i32, skill_get_time2(skl.skill_id, skl.skill_lv) as i64);
            }
            BS_HAMMERFALL => {
                sc::sc_start(c, Some(src), &target, SC_STUN, skl.type_, skl.skill_lv as i32, skill_get_time2(skl.skill_id, skl.skill_lv) as i64);
            }
            MER_LEXDIVINA => {
                sc::sc_start(c, Some(src), &target, SC_SILENCE, skl.type_, skl.skill_lv as i32, skill_get_time2(skl.skill_id, skl.skill_lv) as i64);
            }
            WZ_WATERBALL | WZ_JUPITEL => {
                // WZ_WATERBALL: get the next waterball cell to consume (skill units: skillunit agent)
                let unit_found = if skl.skill_id == WZ_WATERBALL { super::iface::waterball_next_unit(c, src, skl.skill_lv, skl.x, skl.y, skl.type_) } else { None };
                // Official behaviour is to hit as long as there is a line of sight, regardless of distance
                if skl.type_ > 0 && !sc::status_isdead(c, &target) && crate::path::path_search_long(src.map, src.x, src.y, target.x, target.y, crate::map::CellChk::ChkWall) {
                    // Apply canact delay here to prevent hacks (unlimited casting)
                    if let Some(mut s) = unit::get(c, src.id) {
                        s.ud.canact_tick = (tick + amotion_of(c, &s) as i64).max(s.ud.canact_tick);
                        unit::save(c, &s);
                    }
                    let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                    skill_attack(c, BF_MAGIC, &s, &s, &target, skl.skill_id, skl.skill_lv, tick, skl.flag);
                }
                let target_dead = unit::get(c, target.id).map(|t| sc::status_isdead(c, &t)).unwrap_or(true);
                let src_now = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                if unit_found.is_some() && !target_dead && !sc::status_isdead(c, &src_now) {
                    super::iface::skill_delunit(c, unit_found.unwrap_or(0)); // Consume unit for next waterball
                    // Timer will continue and walkdelay set until target is dead, even if there is currently no line of sight
                    crate::battle::unit_attack::unit_set_walkdelay(c, src.id, tick, TIMERSKILL_INTERVAL, 1, 0);
                    let s = unit::get(c, src.id).unwrap_or(src_now);
                    skill_addtimerskill(c, &s, tick + TIMERSKILL_INTERVAL, target.id, skl.x, skl.y, skl.skill_id, skl.skill_lv, skl.type_ + 1, skl.flag);
                } else if let Some(mut e) = sc::get_sce(c, src.id, SC_SPIRIT) {
                    if e.val2 == SL_WIZARD as i32 && e.val3 == skl.skill_id as i32 {
                        e.val3 = 0; // Clear bounced spell check.
                        sc::put_sce(c, &e);
                    }
                }
            }
            NPC_DANCINGBLADE_ATK => {
                skill_attack(c, BF_WEAPON, src, src, &target, skl.skill_id, skl.skill_lv, tick, skl.flag);
                if skl.type_ < 4 {
                    let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                    let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                    let nbl = crate::battle::target::battle_getenemyarea(c, &s, t.x, t.y, 5, area::splash_target(&s), s.id);
                    let next = nbl.map(|n| n.id).unwrap_or(t.id);
                    skill_addtimerskill(c, &s, tick + 650, next, skl.x, 0, NPC_DANCINGBLADE_ATK, skl.skill_lv, skl.type_ + 1, 0);
                }
            }
            WL_CHAINLIGHTNING_ATK => {
                let type_ = skl.type_ + 1;
                // Hit a Lightning on the current Target (start with mflag = 9 - skl->type = 8)
                skill_attack(c, BF_MAGIC, src, src, &target, skl.skill_id, skl.skill_lv, tick, 9 - type_);
                if type_ < 4 + skl.skill_lv as i32 {
                    // Remaining Chains Hit
                    let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                    let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                    // After 1 hit, search for other targets in 7x7 range around current target.
                    let nbl = crate::battle::target::battle_getenemyarea(c, &s, t.x, t.y, 3, area::splash_target(&s), t.id);
                    // The skill hits at least 4 times. Before 4 hits the skill can bounce on the last target again and
                    // again. After 4 hits, the next hit must bounce to a new target.
                    if nbl.is_none() && type_ > 3 {
                        return;
                    }
                    let next = nbl.map(|n| n.id).unwrap_or(t.id);
                    skill_addtimerskill(c, &s, tick + 650, next, 0, 0, WL_CHAINLIGHTNING_ATK, skl.skill_lv, type_, 0);
                }
            }
            WL_TETRAVORTEX_FIRE | WL_TETRAVORTEX_WATER | WL_TETRAVORTEX_WIND | WL_TETRAVORTEX_GROUND => {
                super::clif::clif_skill_nodamage(c, Some(src), &target, skl.skill_id, skl.skill_lv as i32, true);
                skill_attack(c, BF_MAGIC, src, src, &target, skl.skill_id, skl.skill_lv, tick, skl.flag | SD_LEVEL | SD_ANIMATION);
                if skl.type_ >= 3 {
                    // Final Hit
                    if let Some(t) = unit::get(c, target.id).filter(|t| !sc::status_isdead(c, t)) {
                        // Final Status Effect
                        let effects = [SC_BURNING, SC_FREEZING, SC_BLEEDING, SC_STUN];
                        let mut applyeffects = Vec::new();
                        let mut k = 0;
                        let mut i = 1;
                        while i <= 8 {
                            if skl.x as i32 & i != 0 {
                                applyeffects.push(effects[k]);
                            }
                            k += 1;
                            i += i;
                        }
                        if !applyeffects.is_empty() {
                            let e = applyeffects[(c.rnd() % applyeffects.len() as u32) as usize];
                            let d = match e {
                                SC_BURNING => 18000,
                                SC_FREEZING => 27000,
                                SC_BLEEDING => 108000,
                                _ => 4500,
                            };
                            sc::sc_start(c, Some(src), &t, e, 100, skl.skill_lv as i32, d);
                        }
                    }
                }
            }
            NPC_REVERBERATION_ATK => {
                skill_castend_damage_id(c, src, &target, skl.skill_id, skl.skill_lv, tick, skl.flag | SD_LEVEL | SD_ANIMATION);
            }
            NPC_FATALMENACE => {
                warp(c, src, -1, skl.x, skl.y);
            }
            SR_WINDMILL => {
                if target.kind == BL_PC && !crate::inventory::pc_issit(c, target.id) {
                    pc_setsit(c, target.id);
                    super::iface::skill_sit(c, target.id, true);
                    if let Some(t) = unit::get(c, target.id) {
                        crate::clif::sitting(c, &t);
                    }
                }
            }
            SR_KNUCKLEARROW => {
                skill_attack(c, BF_WEAPON, src, src, &target, skl.skill_id, skl.skill_lv, tick, skl.flag | SD_LEVEL);
            }
            CH_PALMSTRIKE => {
                let hidden = |u: &Unit| u.sc.option & OPTION_HIDE != 0;
                if hidden(&target) || hidden(src) {
                    super::attack::skill_blown(c, src, &target, skill_get_blewcount(skl.skill_id, skl.skill_lv), -1, super::attack::BLOWN_NONE);
                } else {
                    skill_attack(c, skl.type_, src, src, &target, skl.skill_id, skl.skill_lv, tick, skl.flag);
                }
            }
            // For SR_FLASHCOMBO
            SR_DRAGONCOMBO | SR_FALLENEMPIRE | SR_TIGERCANNON => {
                if src.kind == BL_PC {
                    if crate::path::distance((src.x - target.x) as i32, (src.y - target.y) as i32) >= 3 {
                        return;
                    }
                    let lv = crate::skill::checkskill(c, src.id, skl.skill_id) as u16;
                    skill_castend_damage_id(c, src, &target, skl.skill_id, lv, tick, 0);
                }
            }
            SU_SV_ROOTTWIST_ATK => {
                if sc::has_sce(c, target.id, SC_SV_ROOTTWIST) {
                    if crate::path::check_distance((src.x - target.x) as i32, (src.y - target.y) as i32, 32) {
                        // Only damage if caster is within 32x32 area
                        skill_attack(c, skl.type_, src, &target, &target, skl.skill_id, skl.skill_lv, tick, skl.flag);
                    }
                    let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                    skill_addtimerskill(c, &s, tick + 1000, target.id, 0, 0, skl.skill_id, skl.skill_lv, skl.type_, skl.flag);
                }
            }
            NPC_PULSESTRIKE2 => {
                skill_castend_damage_id(c, src, &target, skl.skill_id, skl.skill_lv, tick, skl.flag);
            }
            HVAN_EXPLOSION => {
                crate::status::hp::status_kill(c, src.id);
            }
            _ => {
                skill_attack(c, skl.type_, src, src, &target, skl.skill_id, skl.skill_lv, tick, skl.flag);
            }
        }
    } else {
        if src.map != skl.map {
            return;
        }
        match skl.skill_id {
            GN_CRAZYWEED_ATK | WL_EARTHSTRAIN => {
                if skl.skill_id == GN_CRAZYWEED_ATK {
                    let i = skill_get_unit_range(skl.skill_id, skl.skill_lv) as i16;
                    super::iface::skill_cell_overlap_area(c, src, src.map, skl.x - i, skl.y - i, skl.x + i, skl.y + i, skl.skill_id, Wall::Config);
                }
                // [[fallthrough]]
                super::iface::skill_unitsetting(c, src, skl.skill_id, skl.skill_lv, skl.x, skl.y, (skl.type_ << 16) | skl.flag);
            }
            RL_FIRE_RAIN => {
                let i = skill_get_splash(skl.skill_id, skl.skill_lv) as i16;
                if ((c.rnd() % 100) as i32) < 15 + 5 * skl.skill_lv as i32 {
                    super::iface::skill_cell_overlap_area(c, src, src.map, skl.x - i, skl.y - i, skl.x + i, skl.y + i, skl.skill_id, Wall::None);
                }
                super::iface::skill_unitsetting(c, src, skl.skill_id, skl.skill_lv, skl.x, skl.y, 0);
            }
            NPC_MAGMA_ERUPTION | NC_MAGMA_ERUPTION => {
                super::iface::skill_unitsetting(c, src, skl.skill_id, skl.skill_lv, skl.x, skl.y, 0);
            }
            NW_GRENADES_DROPPING => {
                let area_ = skill_get_splash(skl.skill_id, skl.skill_lv);
                let tmpx = skl.x - area_ as i16 + (c.rnd() % (area_ * 2 + 1) as u32) as i16;
                let tmpy = skl.y - area_ as i16 + (c.rnd() % (area_ * 2 + 1) as u32) as i16;
                super::iface::skill_unitsetting(c, src, skl.skill_id, skl.skill_lv, tmpx, tmpy, skl.flag);
            }
            HN_METEOR_STORM_BUSTER => {
                let area_ = 4i16;
                let tmpx = c.rnd_value((skl.x - area_) as i32, (skl.x + area_) as i32) as i16;
                let tmpy = c.rnd_value((skl.y - area_) as i32, (skl.y + area_) as i32) as i16;
                if super::iface::map_getcell_landprotector(c, src.map, tmpx, tmpy) {
                    return;
                }
                let splash = skill_get_splash(skl.skill_id, skl.skill_lv) as i16;
                super::clif::clif_skill_poseffect(c, src, skl.skill_id, skl.skill_lv, tmpx, tmpy, tick);
                let f = skl.flag | BCT_ENEMY | SD_SPLASH | SKILL_ALTDMG_FLAG | 1;
                foreach_in_area(c, src.map, tmpx - splash, tmpy - splash, tmpx + splash, tmpy + splash, BL_CHAR, Wall::Config, |c, bl| {
                    area::skill_area_sub(c, bl, src, skl.skill_id, skl.skill_lv, tick, f, skill_castend_damage_id)
                });
                let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                super::iface::skill_unitsetting(c, &s, skl.skill_id, skl.skill_lv, tmpx, tmpy, skill_get_unit_interval(skl.skill_id));
            }
            NW_HASTY_FIRE_IN_THE_HOLE => {
                super::iface::skill_castend_pos2(c, src, skl.x, skl.y, skl.skill_id, skl.skill_lv, tick, skl.flag);
            }
            NW_MISSION_BOMBARD => {
                let range = skill_get_splash(skl.skill_id, skl.skill_lv);
                let tmpx = skl.x - range as i16 + (c.rnd() % (range * 2 + 1) as u32) as i16;
                let tmpy = skl.y - range as i16 + (c.rnd() % (range * 2 + 1) as u32) as i16;
                let r = range as i16;
                let f = skl.flag | BCT_ENEMY | SD_SPLASH | 1;
                foreach_in_area(c, src.map, tmpx - r, tmpy - r, tmpx + r, tmpy + r, BL_CHAR, Wall::Config, |c, bl| {
                    area::skill_area_sub(c, bl, src, skl.skill_id, skl.skill_lv, tick, f, skill_castend_damage_id)
                });
            }
            SKE_TWINKLING_GALAXY => {
                let area_ = skill_get_unit_range(skl.skill_id, skl.skill_lv);
                let splash = skill_get_splash(skl.skill_id, skl.skill_lv) as i16;
                let tmpx = skl.x - area_ as i16 + (c.rnd() % (area_ * 2 + 1) as u32) as i16;
                let tmpy = skl.y - area_ as i16 + (c.rnd() % (area_ * 2 + 1) as u32) as i16;
                let f = skl.flag | BCT_ENEMY | SD_SPLASH | 1;
                foreach_in_area(c, src.map, tmpx - splash, tmpy - splash, tmpx + splash, tmpy + splash, BL_CHAR, Wall::Config, |c, bl| {
                    area::skill_area_sub(c, bl, src, skl.skill_id, skl.skill_lv, tick, f, skill_castend_damage_id)
                });
            }
            SKE_STAR_CANNON => {
                let area_ = skill_get_unit_range(skl.skill_id, skl.skill_lv);
                let splash = skill_get_splash(skl.skill_id, skl.skill_lv) as i16;
                let stars = (skl.skill_lv / 4) + 1;
                for _ in 0..stars {
                    let tmpx = skl.x - area_ as i16 + (c.rnd() % (area_ * 2 + 1) as u32) as i16;
                    let tmpy = skl.y - area_ as i16 + (c.rnd() % (area_ * 2 + 1) as u32) as i16;
                    let f = skl.flag | BCT_ENEMY | SD_SPLASH | 1;
                    foreach_in_area(c, src.map, tmpx - splash, tmpy - splash, tmpx + splash, tmpy + splash, BL_CHAR, Wall::Config, |c, bl| {
                        area::skill_area_sub(c, bl, src, skl.skill_id, skl.skill_lv, tick, f, skill_castend_damage_id)
                    });
                }
            }
            _ => {}
        }
    }
}

/// clif_skill_damage(src, src, tick, amotion, 0, DMGVAL_IGNORE, 1, id, lv, DMG_SINGLE) (SR_SKYNETBLOW's dummy).
fn clif_damage_ignore(c: &Ctx, src: &Unit, tick: Tick, skill_id: u16, skill_lv: u16) {
    super::clif::clif_skill_damage(c, src, src, tick, amotion_of(c, src), 0, super::clif::DMGVAL_IGNORE, 1, skill_id, skill_lv as i32, DMG_SINGLE);
}

/// pc.hpp pc_setsit (status2's port in status/sc_ext.rs).
fn pc_setsit(c: &Ctx, aid: u32) {
    crate::status::sc_ext::pc_setsit(c, aid);
}

/// unit_warp(bl, m, x, y, CLR_TELEPORT) for any unit.
fn warp(c: &Ctx, u: &Unit, m: i32, x: i16, y: i16) -> i32 {
    use crate::script::buildins::script2::{unitctl::Bl, unitmove};
    let Some(u) = unit::get(c, u.id) else { return 1 };
    unitmove::unit_warp(c, &Bl::Unit(u), m, x, y, unit::CLR_TELEPORT)
}
