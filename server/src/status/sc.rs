//! status.cpp's status change framework: the per-unit status_change (entries in `sc_entry`, opt1..option in the unit
//! row's `sc`, the rest in `sc_unit`), status_change_start / status_change_start_post_delay / status_change_end /
//! status_change_timer / status_change_clear, status_calc_state, status_display_add / remove,
//! status_change_clear_onChangeMap, status_get_sc_def and status_change_isDisabledOnMap.
//!
//! The big per-SC `switch (type)` blocks of those functions are in their own files, every case ported: sc_immune.rs
//! (immunities), sc_vals_a/b/c.rs (value setup), sc_end_fx.rs (end effects), sc_timer_fx.rs (periodic effects),
//! sc_clear.rs (status_change_timer_sub, clear_buffs, spread); the state they share is sc_st.rs.

use spacetimedb::Table;

use super::consts::*;
use super::data::{self, sc_entry, sc_unit, ScDisplay, ScEntry, ScUnit};
use super::defs::*;
use crate::db::misc::status::{status_db, StatusChangeDb, StatusDatabase};
use crate::timer::{self, Func};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// timer.hpp INFINITE_TICK.
pub const INFINITE_TICK: i64 = -1;

/// A unit's status_change entries (a snapshot; changes go through the functions below).
#[derive(Clone, Debug, Default)]
pub struct Sc {
    pub list: Vec<ScEntry>,
}

impl Sc {
    /// status_change::empty.
    pub fn empty(&self) -> bool {
        self.list.is_empty()
    }
    /// status_change::getSCE.
    pub fn get(&self, t: i32) -> Option<&ScEntry> {
        self.list.iter().find(|e| e.sc_type == t)
    }
    pub fn has(&self, t: i32) -> bool {
        self.get(t).is_some()
    }
    /// sce->val1 .. val4 of a present status (0 when absent: callers test presence first, as rAthena does).
    pub fn v1(&self, t: i32) -> i32 {
        self.get(t).map(|e| e.val1).unwrap_or(0)
    }
    pub fn v2(&self, t: i32) -> i32 {
        self.get(t).map(|e| e.val2).unwrap_or(0)
    }
    pub fn v3(&self, t: i32) -> i32 {
        self.get(t).map(|e| e.val3).unwrap_or(0)
    }
    pub fn v4(&self, t: i32) -> i32 {
        self.get(t).map(|e| e.val4).unwrap_or(0)
    }
    pub fn types(&self) -> Vec<i32> {
        self.list.iter().map(|e| e.sc_type).collect()
    }
}

thread_local! {
    /// Per-call cache of a unit's sorted `sc_entry` list (`load` / `get_sce` / `has_sce`), emptied by `reset_caches`
    /// (`status::data::reset_work`).
    static SC_CACHE: std::cell::RefCell<std::collections::HashMap<u32, Vec<ScEntry>>> =
        std::cell::RefCell::new(std::collections::HashMap::new());
    /// Per-call write-through cache of `sc_unit` rows (`unit_sc` / `cant` / `put_unit_sc`), emptied by `reset_caches`.
    static SC_UNIT_CACHE: std::cell::RefCell<std::collections::HashMap<u32, Option<ScUnit>>> =
        std::cell::RefCell::new(std::collections::HashMap::new());
}

pub fn reset_caches() {
    SC_CACHE.with(|m| m.borrow_mut().clear());
    SC_UNIT_CACHE.with(|m| m.borrow_mut().clear());
}

/// status_get_sc: the unit's status changes, in sc_type order (rAthena iterates its unordered_map; the order only
/// matters for statuses with scripts).
pub fn load(c: &Ctx, bl: u32) -> Sc {
    if let Some(list) = SC_CACHE.with(|m| m.borrow().get(&bl).cloned()) {
        return Sc { list };
    }
    let mut list: Vec<ScEntry> = c.db().sc_entry().by_bl().filter(bl).collect();
    list.sort_by_key(|e| e.sc_type);
    SC_CACHE.with(|m| {
        m.borrow_mut().insert(bl, list.clone());
    });
    Sc { list }
}

pub fn get_sce(c: &Ctx, bl: u32, t: i32) -> Option<ScEntry> {
    if let Some(found) = SC_CACHE.with(|m| m.borrow().get(&bl).map(|v| v.iter().find(|e| e.sc_type == t).cloned())) {
        return found;
    }
    load(c, bl).list.into_iter().find(|e| e.sc_type == t)
}

pub fn has_sce(c: &Ctx, bl: u32, t: i32) -> bool {
    if let Some(found) = SC_CACHE.with(|m| m.borrow().get(&bl).map(|v| v.iter().any(|e| e.sc_type == t))) {
        return found;
    }
    load(c, bl).list.iter().any(|e| e.sc_type == t)
}

/// Writes changed vals of an existing entry.
pub fn put_sce(c: &Ctx, e: &ScEntry) {
    SC_CACHE.with(|m| m.borrow_mut().remove(&e.bl_id));
    crate::unit::bump_mob_pos_gen();
    if c.db().sc_entry().id().find(e.id).is_some() {
        c.db().sc_entry().id().update(e.clone());
    }
}

pub fn unit_sc(c: &Ctx, bl: u32) -> ScUnit {
    if let Some(cached) = SC_UNIT_CACHE.with(|m| m.borrow().get(&bl).cloned()) {
        return cached.unwrap_or(ScUnit { bl_id: bl, ..Default::default() });
    }
    let row = c.db().sc_unit().bl_id().find(bl);
    SC_UNIT_CACHE.with(|m| {
        m.borrow_mut().insert(bl, row.clone());
    });
    row.unwrap_or(ScUnit { bl_id: bl, ..Default::default() })
}

pub fn put_unit_sc(c: &Ctx, s: &ScUnit) {
    let cached = SC_UNIT_CACHE.with(|m| m.borrow().get(&s.bl_id).cloned());
    match cached {
        Some(Some(ref old)) if old == s => return,
        Some(Some(_)) => {
            c.db().sc_unit().bl_id().update(s.clone());
        }
        Some(None) => {
            c.db().sc_unit().insert(s.clone());
        }
        None => match c.db().sc_unit().bl_id().find(s.bl_id) {
            Some(old) if old == *s => {
                SC_UNIT_CACHE.with(|m| {
                    m.borrow_mut().insert(s.bl_id, Some(s.clone()));
                });
                return;
            }
            Some(_) => {
                c.db().sc_unit().bl_id().update(s.clone());
            }
            None => {
                c.db().sc_unit().insert(s.clone());
            }
        },
    }
    crate::unit::bump_mob_pos_gen();
    SC_UNIT_CACHE.with(|m| {
        m.borrow_mut().insert(s.bl_id, Some(s.clone()));
    });
}

/// sc->deleteSCE(type): the entry goes (and leaves the unordered_map's order).
fn delete_sce(c: &Ctx, bl_id: u32, e: &ScEntry) {
    SC_CACHE.with(|m| m.borrow_mut().remove(&bl_id));
    crate::unit::bump_mob_pos_gen();
    c.db().sc_entry().id().delete(e.id);
    let mut us = unit_sc(c, bl_id);
    super::sc_umap::erase(&mut us, e.sc_type);
    put_unit_sc(c, &us);
}

/// The unit's status changes in the iteration order of rAthena's unordered_map (`for (auto& it : *sc)`: the SC
/// scripts of status_calc_pc_sub, chrif_save_scdata).
pub fn load_map_order(c: &Ctx, bl: u32) -> Vec<ScEntry> {
    let sc = load(c, bl);
    let us = unit_sc(c, bl);
    let mut out: Vec<ScEntry> = us.umap_order.iter().filter_map(|&t| sc.get(t).cloned()).collect();
    // (an entry missing from the order would be a bug; keep it rather than lose it)
    for e in sc.list {
        if !out.iter().any(|x| x.sc_type == e.sc_type) {
            log::error!("status_change: status {} of unit {bl} is not in the map order", e.sc_type);
            out.push(e);
        }
    }
    out
}

/// Everything status_change keeps for a unit goes with it (unit_free).
pub fn free_unit(c: &Ctx, bl: u32) {
    SC_CACHE.with(|m| m.borrow_mut().remove(&bl));
    SC_UNIT_CACHE.with(|m| {
        m.borrow_mut().insert(bl, None);
    });
    crate::unit::bump_mob_pos_gen();
    let ids: Vec<u64> = c.db().sc_entry().by_bl().filter(bl).map(|e| e.id).collect();
    for id in ids {
        c.db().sc_entry().id().delete(id);
    }
    c.db().sc_unit().bl_id().delete(bl);
}

/// The cant.* bit of an SCS_NO* flag.
pub fn cant_bit(scs: u32) -> u32 {
    1u32 << scs
}

/// sc->cant.<flag> (SCS_NOMOVE, SCS_NOCHAT, …).
pub fn cant(c: &Ctx, bl: u32, scs: u32) -> bool {
    if let Some(cant_bits) = SC_UNIT_CACHE.with(|m| m.borrow().get(&bl).map(|r| r.as_ref().map(|u| u.cant).unwrap_or(0))) {
        return cant_bits & cant_bit(scs) != 0;
    }
    unit_sc(c, bl).cant & cant_bit(scs) != 0
}

// ---------------------------------------------------------------------------------------------- unit helpers

/// status_isdead.
pub fn status_isdead(c: &Ctx, u: &Unit) -> bool {
    super::status_get_status_data(c, u).hp == 0
}

/// map.hpp mapdata_flag_vs2 / mapdata_flag_gvg2_no_te / mapdata_flag_gvg2_te.
fn mapdata_flag_vs2(c: &Ctx, m: u16) -> bool {
    use crate::npc::*;
    let f = |flag| mapflag(c, m, flag);
    f(MF_PVP) || f(MF_GVG_DUNGEON) || f(MF_GVG) || f(MF_GVG_CASTLE) || f(MF_GVG_TE) || f(MF_GVG_TE_CASTLE) || f(MF_BATTLEGROUND)
}
fn mapdata_flag_gvg2_no_te(c: &Ctx, m: u16) -> bool {
    use crate::npc::*;
    mapflag(c, m, MF_GVG) || mapflag(c, m, MF_GVG_CASTLE)
}
fn mapdata_flag_gvg2_te(c: &Ctx, m: u16) -> bool {
    use crate::npc::*;
    mapflag(c, m, MF_GVG_TE) || mapflag(c, m, MF_GVG_TE_CASTLE)
}

/// status.cpp status_change_isDisabledOnMap_.
fn is_disabled_on_map_(sc_type: i32, map_is_vs: bool, map_is_pvp: bool, map_is_gvg: bool, map_is_bg: bool, map_zone: u32, map_is_te: bool) -> bool {
    if !StatusDatabase::validate_status(sc_type) {
        return false;
    }
    let disabled = status_db().sc_disabled(sc_type) as u32;
    (!map_is_vs && disabled & 1 != 0)
        || (map_is_pvp && disabled & 2 != 0)
        || (map_is_gvg && disabled & 4 != 0)
        || (map_is_bg && disabled & 8 != 0)
        || (map_is_te && disabled & 16 != 0)
        || disabled & map_zone != 0
}

/// The status_change_isDisabledOnMap macro: note that it passes `(m->zone << 3) != 0` (0 or 1) as the zone.
pub fn is_disabled_on_map(c: &Ctx, sc_type: i32, m: u16) -> bool {
    use crate::npc::*;
    let zone = mapflag_value(c, m, MF_RESTRICTED) as u32;
    is_disabled_on_map_(
        sc_type,
        mapdata_flag_vs2(c, m),
        mapflag(c, m, MF_PVP),
        mapdata_flag_gvg2_no_te(c, m),
        mapflag(c, m, MF_BATTLEGROUND),
        ((zone << 3) != 0) as u32,
        mapdata_flag_gvg2_te(c, m),
    )
}

/// battle.cpp battle_check_undead (battle.conf undead_detect_type: 0).
pub fn battle_check_undead(race: i32, element: i32) -> bool {
    let undead_detect_type = crate::conf::undead_detect_type();
    match undead_detect_type {
        0 | 3 => element == ELE_UNDEAD,
        1 => race == RC_UNDEAD,
        _ => element == ELE_UNDEAD || race == RC_UNDEAD,
    }
}

// ---------------------------------------------------------------------------------------------- status_get_sc_def

/// status.cpp status_get_sc_def: the duration left after the target's resistances (0 = resisted). `src` None: the
/// status has no source (status_change_start's callers pass one for every resistible status).
pub fn status_get_sc_def(c: &Ctx, src: Option<&Unit>, bl: &Unit, t: i32, mut rate: i32, mut tick: i64, flag: u8) -> i64 {
    let Some(src) = src else { return if tick != 0 { tick } else { 1 } };
    let mut sc_def: i32 = 0;
    let mut tick_def: i32 = -1;
    let mut sc_def2: i32 = 0;
    let mut tick_def2: i32 = 0;
    // Skills (magic type) that are blocked by Golden Thief Bug card or Wand of Hermod
    if status_isimmune(c, bl) != 0 {
        // battle_getcurrentskill: the source's current skill (a skill unit's: its group's)
        let skill_id = match crate::battle::target::battle_getcurrentskill(c, src) {
            s if crate::db::skills::skill_db().exists(s) => s,
            _ => status_db().get_skill(t),
        };
        if let Some(skill) = crate::db::skills::skill_db().find(skill_id) {
            use crate::db::skills::*;
            let inf = skill.inf;
            if skill.skill_type == BF_MAGIC as u16
                && !skill.has_inf2(INF2_IGNOREGTB)
                && ((inf == INF_ATTACK_SKILL || inf == INF_GROUND_SKILL || inf == INF_SUPPORT_SKILL) || (inf == INF_SELF_SKILL && src.id != bl.id))
            {
                return 0;
            }
        }
    }
    let is_pc = bl.kind == BL_PC;
    let status = super::status_get_status_data(c, bl);
    let sc = load(c, bl.id);
    let sc_opt = if sc.empty() { None } else { Some(&sc) };
    let lv_src = super::status_get_lv(c, src);
    let lv_bl = super::status_get_lv(c, bl);
    let level_adv = level_adv(lv_src, lv_bl);
    let (vit, int_, agi, luk, mdef, str, dex) =
        (status.vit as i32, status.int_ as i32, status.agi as i32, status.luk as i32, status.mdef as i32, status.str as i32, status.dex as i32);
    match t {
        SC_POISON | SC_DPOISON => {
            sc_def = vit * 100 - level_adv;
            tick_def2 = -2000;
        }
        SC_STUN => {
            sc_def = vit * 100 - level_adv;
            tick_def2 = -500;
        }
        SC_SILENCE => {
            sc_def = int_ * 100 - level_adv;
            tick_def2 = -2000;
        }
        SC_BLEEDING => {
            sc_def = agi * 100 - level_adv;
            tick_def2 = -12000;
        }
        SC_SLEEP => {
            sc_def = agi * 100 - level_adv;
            tick_def2 = -2000;
        }
        SC_STONEWAIT | SC_FREEZE => {
            sc_def = mdef * 100 - level_adv;
            tick_def2 = -3000;
        }
        SC_CURSE => {
            // Special property: immunity when luk is zero
            if luk == 0 {
                return 0;
            }
            sc_def = luk * 100 - level_adv;
            tick_def2 = -2000;
        }
        SC_BLIND => {
            sc_def = int_ * 100 - level_adv;
            tick_def2 = -2000;
        }
        SC_CONFUSION => {
            sc_def = luk * 100 - level_adv;
            tick_def2 = -2000;
        }
        SC_DECREASEAGI => {
            if is_pc {
                tick /= 2; // Half duration for players.
            }
            sc_def2 = mdef * 100;
        }
        SC_JOINTBEAT => tick_def2 = 1000 * ((luk / 2 + agi / 5) / 2),
        SC_DEEPSLEEP => {
            let b = super::status_get_base_status(c, bl);
            tick_def2 = b.int_ as i32 * 25 + lv_bl * 50;
        }
        SC_NETHERWORLD => tick_def2 = lv_bl * 20 + super::job_level_or_1(c, bl) * 100,
        SC_MARSHOFABYSS => tick_def2 = (int_ + luk) * 50,
        SC_STASIS => tick_def2 = (vit + dex) * 50,
        SC_WHITEIMPRISON => {
            if src.id != bl.id {
                sc_def = str * 20 + lv_bl * 20 + luk * 10;
                tick_def2 = -2000;
            }
        }
        SC_FEAR => {
            sc_def = int_ * 20 + lv_bl * 20 + luk * 10;
            tick_def2 = -4000;
        }
        SC_BURNING => {
            sc_def = agi * 20 + lv_bl * 20 + luk * 10;
            tick_def2 = -2000;
        }
        SC_FREEZING => tick_def2 = (vit + dex) * 50,
        SC_OBLIVIONCURSE => {
            sc_def = int_ * 80;
            sc_def = sc_def.max(500);
            tick_def = 0;
            tick_def2 = (vit + luk) * 500;
        }
        SC_TOXIN | SC_PARALYSE | SC_VENOMBLEED | SC_MAGICMUSHROOM | SC_DEATHHURT | SC_PYREXIA | SC_LEECHESEND => tick_def2 = (vit + luk) * 500,
        SC_BITE => sc_def2 = agi * 25,
        SC_ELECTRICSHOCKER => tick_def2 = (vit + agi) * 70,
        SC_CRYSTALIZE => tick_def2 = super::status_get_base_status(c, bl).vit as i32 * 100,
        SC_VACUUM_EXTREME => tick_def2 = super::base_str_of(c, bl) * 50,
        SC_KYOUGAKU => tick_def2 = 30 * int_,
        SC_PARALYSIS => tick_def2 = (vit + luk) * 50,
        SC_VOICEOFSIREN => tick_def2 = lv_bl * 100 + super::job_level_or_1(c, bl) * 200,
        SC_B_TRAP => tick_def = super::base_str_of(c, bl) * 50,
        SC_NORECOVER_STATE => tick_def2 = luk * 100,
        _ => {
            // Effect that cannot be reduced? Likely a buff.
            if (c.rnd() % 10000) as i32 >= rate {
                return 0;
            }
            return if tick != 0 { tick } else { 1 };
        }
    }
    if is_pc {
        let rate_conf = crate::conf::pc_sc_def_rate();
        if rate_conf != 100 {
            sc_def = sc_def * rate_conf / 100;
            sc_def2 = sc_def2 * rate_conf / 100;
        }
        sc_def = cap_value(sc_def, 0, crate::conf::pc_max_sc_def() * 100);
        sc_def2 = cap_value(sc_def2, 0, crate::conf::pc_max_sc_def() * 100);
        if rate_conf != 100 {
            tick_def = tick_def * rate_conf / 100;
            tick_def2 = tick_def2 * rate_conf / 100;
        }
    } else {
        let rate_conf = crate::conf::mob_sc_def_rate();
        if rate_conf != 100 {
            sc_def = sc_def * rate_conf / 100;
            sc_def2 = sc_def2 * rate_conf / 100;
        }
        sc_def = cap_value(sc_def, 0, crate::conf::mob_max_sc_def() * 100);
        sc_def2 = cap_value(sc_def2, 0, crate::conf::mob_max_sc_def() * 100);
        if rate_conf != 100 {
            tick_def = tick_def * rate_conf / 100;
            tick_def2 = tick_def2 * rate_conf / 100;
        }
    }
    if let Some(sc) = sc_opt {
        if sc.has(SC_SCRESIST) {
            sc_def += sc.v1(SC_SCRESIST) * 100; // Status resist
        } else if sc.has(SC_SIEGFRIED) && matches!(t, SC_BLIND | SC_STONE | SC_FREEZE | SC_STUN | SC_CURSE | SC_SLEEP | SC_SILENCE) {
            sc_def += sc.v3(SC_SIEGFRIED) * 100; // Status resistance.
        } else if sc.has(SC_LEECHESEND) && sc.v3(SC_LEECHESEND) == 0 {
            if matches!(t, SC_BLIND | SC_STUN) {
                return 0; // Immune
            }
        } else if sc.has(SC_OBLIVIONCURSE) && sc.v3(SC_OBLIVIONCURSE) == 0 && matches!(t, SC_SILENCE | SC_CURSE) {
            return 0; // Immune
        }
    }
    // When tick def not set, reduction is the same for both.
    if tick_def == -1 {
        tick_def = sc_def;
    }
    let reseff = if is_pc { data::sd_get(c, bl.id).map(|sd| sd.lists.reseff).unwrap_or_default() } else { Vec::new() };
    // Natural resistance
    if flag & SCSTART_NORATEDEF == 0 {
        rate -= rate * sc_def / 10000;
        rate -= sc_def2;
        // Item resistance (only applies to rate%)
        for it in &reseff {
            if it.id as i32 == t {
                rate -= rate * it.val / 10000;
            }
        }
        // Aegis accuracy
        if rate > 0 && rate % 10 != 0 {
            rate += 10 - rate % 10;
        }
    }
    let Some(scdb) = status_db().find(t) else { return 0 };
    // Cap minimum rate
    rate = rate.max(scdb.min_rate as i32);
    if rate < 10000 && (rate <= 0 || (c.rnd() % 10000) as i32 >= rate) {
        return 0;
    }
    // Duration cannot be reduced
    if flag & SCSTART_NOTICKDEF != 0 {
        return tick.max(scdb.min_duration);
    }
    tick -= tick * tick_def as i64 / 10000;
    // Renewal applies item resistance also to duration
    for it in &reseff {
        if it.id as i32 == t {
            tick -= tick * it.val as i64 / 10000;
        }
    }
    tick -= tick_def2 as i64;
    tick.max(scdb.min_duration)
}

/// status.cpp status_isimmune.
pub fn status_isimmune(c: &Ctx, bl: &Unit) -> i32 {
    let sc = load(c, bl.id);
    if sc.has(SC_HERMODE) {
        return 100;
    }
    if sc.has(SC_DEADLY_DEFEASANCE) {
        return 0;
    }
    if bl.kind == BL_PC {
        if let Some(sd) = data::sd_get(c, bl.id) {
            if sd.special_state.no_magic_damage as i32 >= crate::conf::gtb_sc_immunity() {
                return sd.special_state.no_magic_damage as i32;
            }
        }
    }
    0
}

// ---------------------------------------------------------------------------------------------- start

/// sc_start (rate in 1..100).
pub fn sc_start(c: &Ctx, src: Option<&Unit>, bl: &Unit, t: i32, rate: i32, val1: i32, duration: i64) -> bool {
    status_change_start(c, src, bl, t, 100 * rate, val1, 0, 0, 0, duration, SCSTART_NONE, 0)
}

/// sc_start2.
pub fn sc_start2(c: &Ctx, src: Option<&Unit>, bl: &Unit, t: i32, rate: i32, val1: i32, val2: i32, duration: i64) -> bool {
    status_change_start(c, src, bl, t, 100 * rate, val1, val2, 0, 0, duration, SCSTART_NONE, 0)
}

/// sc_start4.
#[allow(clippy::too_many_arguments)]
pub fn sc_start4(c: &Ctx, src: Option<&Unit>, bl: &Unit, t: i32, rate: i32, val1: i32, val2: i32, val3: i32, val4: i32, duration: i64) -> bool {
    status_change_start(c, src, bl, t, 100 * rate, val1, val2, val3, val4, duration, SCSTART_NONE, 0)
}

/// status.cpp status_change_start.
#[allow(clippy::too_many_arguments)]
pub fn status_change_start(c: &Ctx, src: Option<&Unit>, bl: &Unit, t: i32, rate: i32, val1: i32, val2: i32, mut val3: i32, val4: i32, duration: i64, flag: u8, mut delay: i32) -> bool {
    let Some(scdb) = status_db().find(t) else {
        log::error!("status_change_start: Invalid status change ({t})!");
        return false;
    };
    // (every unit has a status_change)
    // Scripted status changes only work for players for the time being
    if scdb.script.is_some() && bl.kind != BL_PC {
        log::error!("status_change_start: Failed to start the scripted status change {t} on a non player.");
        return false;
    }
    if bl.kind != BL_NPC && status_isdead(c, bl) && t != SC_NOCHAT && t != SC_JAILED {
        return false;
    }
    if is_disabled_on_map(c, t, bl.map) {
        return false;
    }
    let sc = load(c, bl.id);
    if sc.has(SC_GRAVITYCONTROL) {
        return false;
    }
    // Fail if Madogear is active
    let option = unit::get(c, bl.id).map(|u| u.sc.option).unwrap_or(bl.sc.option);
    if option & OPTION_MADOGEAR != 0 && flag & SCSTART_NOAVOID != 0 && scdb.has_flag(SCF_FAILEDMADO) {
        return false;
    }
    let status = super::status_get_status_data(c, bl);
    // Check for Boss resistances
    if status.mode & MD_STATUSIMMUNE != 0 && flag & SCSTART_NOAVOID == 0 && scdb.has_flag(SCF_BOSSRESIST) {
        return false;
    }
    // Check for MVP resistance
    if status.mode & MD_MVP != 0 && flag & SCSTART_NOAVOID == 0 && scdb.has_flag(SCF_MVPRESIST) {
        return false;
    }
    // End the SCs from the list and immediately return
    if !scdb.endreturn.is_empty() {
        let mut is_removed = false;
        for &rem_sc in &scdb.endreturn {
            if has_sce(c, bl.id, rem_sc) {
                status_change_end(c, bl.id, rem_sc, 0);
                is_removed = true;
            }
        }
        if is_removed {
            return true; // Return true so that sc_start can be checked as success
        }
    }
    // Check failing SCs from list
    if !scdb.fail.is_empty() {
        let last_effect = unit_sc(c, bl.id).last_effect;
        for &f in &scdb.fail {
            // Don't let OPT1 that have RemoveOnDamaged start a new effect in the same attack.
            if has_sce(c, bl.id, f) || last_effect == f {
                return false;
            }
        }
    }
    let mut duration = duration;
    // Adjust tick according to status resistances
    if flag & (SCSTART_NOAVOID | SCSTART_LOADED) == 0 {
        duration = status_get_sc_def(c, src, bl, t, rate, duration, flag);
        if duration == 0 {
            return false;
        }
    }
    let mut tick = duration as i32;
    // Type-specific checks that need to happen before the delay
    match t {
        SC_STONE | SC_STONEWAIT | SC_FREEZE => {
            if flag & SCSTART_NOAVOID == 0 {
                if crate::conf::undead_detect_type() == 0 && status.race as i32 == RC_UNDEAD {
                    return false;
                }
                if battle_check_undead(status.race as i32, status.def_ele as i32) {
                    return false;
                }
            }
            if t == SC_STONEWAIT {
                // Stonewait has a unique handling where the delay is actually the duration until stone kicks in
                val3 = 1.max(tick - delay); // Petrify time
                tick = delay;
                delay = 0;
            }
        }
        SC_BLEEDING => delay = 0,
        SC_BURNING => {
            if status.def_ele as i32 == ELE_FIRE && status.ele_lv == 2 {
                return false;
            }
        }
        _ => {}
    }
    if delay <= 0 {
        return start_post_delay(c, src.map(|s| s.id), bl.id, t, val1, val2, val3, val4, tick, flag);
    }
    // RENEWAL: the delay is substracted from the duration
    let entry = ScDelay {
        id: 0,
        src_id: src.map(|s| s.id).unwrap_or(0),
        bl_id: bl.id,
        sc_type: t,
        val1,
        val2,
        val3,
        val4,
        tick: 1.max(tick - delay),
        flag,
    };
    let row = c.db().sc_delay().insert(entry);
    timer::add(c, c.tick() + delay as i64, Func::StatusChangeStartDelay, 0, row.id as i64);
    true
}

/// status.cpp s_delay_status: a status change waiting for its delay (status_change_start_timer).
#[spacetimedb::table(accessor = sc_delay)]
#[derive(Clone, Debug)]
pub struct ScDelay {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub src_id: u32,
    pub bl_id: u32,
    pub sc_type: i32,
    pub val1: i32,
    pub val2: i32,
    pub val3: i32,
    pub val4: i32,
    pub tick: i32,
    pub flag: u8,
}

/// status.cpp status_change_start_timer.
pub fn start_timer(c: &Ctx, index: u64) {
    let Some(entry) = c.db().sc_delay().id().find(index) else { return };
    c.db().sc_delay().id().delete(index);
    let Some(bl) = unit::get(c, entry.bl_id) else { return };
    if !status_isdead(c, &bl) {
        let src = if entry.src_id > 0 { unit::get(c, entry.src_id).map(|u| u.id) } else { None };
        start_post_delay(c, src, entry.bl_id, entry.sc_type, entry.val1, entry.val2, entry.val3, entry.val4, entry.tick, entry.flag);
    }
}

/// status.cpp status_change_start_post_delay.
#[allow(clippy::too_many_arguments)]
fn start_post_delay(c: &Ctx, src: Option<u32>, bl_id: u32, t: i32, val1: i32, val2: i32, val3: i32, val4: i32, tick: i32, flag: u8) -> bool {
    let Some(bl) = unit::get(c, bl_id) else { return false };
    let is_pc = bl.kind == BL_PC;
    let Some(scdb) = status_db().find(t).cloned() else { return false };
    let status = super::status_get_status_data(c, &bl);
    let undead_flag = battle_check_undead(status.race as i32, status.def_ele as i32);
    let mut st = super::sc_st::StartSt {
        c,
        src: src.and_then(|s| unit::get(c, s)),
        bl: bl.clone(),
        t,
        val1,
        val2,
        val3,
        val4,
        tick,
        tick_time: 0,
        flag,
        scdb: scdb.clone(),
        undead_flag,
    };
    // Check for immunities / sc fails
    if let super::sc_st::Flow::Return(r) = super::sc_immune::immunity(&mut st) {
        return r;
    }
    let (mut val1, mut val2, mut val3, mut val4, mut tick, flag) = (st.val1, st.val2, st.val3, st.val4, st.tick, st.flag);
    let Some(bl) = unit::get(c, bl_id) else { return false };
    // Check for OPT1 stacking
    if bl.sc.opt1 > OPT1_NONE && scdb.opt1 as i16 > OPT1_NONE {
        for s in status_db().iter() {
            let opt1_type = s.type_;
            if has_sce(c, bl_id, opt1_type) && s.opt1 as i16 > OPT1_NONE {
                status_change_end(c, bl_id, opt1_type, 0);
            }
        }
    }
    // Before overlapping fail, one must check for status cured.
    let endlist: Vec<i32> = if t == SC_BERSERK && val3 == SC__BLOODYLUST { status_db().get_end_on_start(SC__BLOODYLUST).to_vec() } else { scdb.endonstart.clone() };
    // End the SCs from the list
    for rem_sc in endlist {
        if let Some(mut e) = get_sce(c, bl_id, rem_sc) {
            if rem_sc == SC_BERSERK || rem_sc == SC_SATURDAYNIGHTFEVER {
                e.val2 = 0; // Mark to not lose hp
                put_sce(c, &e);
            }
            status_change_end(c, bl_id, rem_sc, 0);
        }
    }
    // List of hardcoded status cured.
    match t {
        SC_BLESSING => {
            if is_pc {
                // Remove Curse first, Stone is only removed if the target is not cursed
                if has_sce(c, bl_id, SC_CURSE) {
                    status_change_end(c, bl_id, SC_CURSE, 0);
                    return true;
                } else if has_sce(c, bl_id, SC_STONE) {
                    status_change_end(c, bl_id, SC_STONE, 0);
                    return true;
                }
            }
            if get_sce(c, bl_id, SC_SPIRIT).map(|e| e.val2 == crate::db::skills::SL_HIGH as i32).unwrap_or(false) {
                status_change_end(c, bl_id, SC_SPIRIT, 0);
            }
        }
        SC_INCREASEAGI => {
            if get_sce(c, bl_id, SC_SPIRIT).map(|e| e.val2 == crate::db::skills::SL_HIGH as i32).unwrap_or(false) {
                status_change_end(c, bl_id, SC_SPIRIT, 0);
            }
        }
        SC_DELUGE => {
            if has_sce(c, bl_id, SC_FOGWALL) && has_sce(c, bl_id, SC_BLIND) {
                status_change_end(c, bl_id, SC_BLIND, 0);
            }
        }
        SC_SILENCE => {
            if get_sce(c, bl_id, SC_GOSPEL).map(|e| e.val4 == BCT_SELF).unwrap_or(false) {
                status_change_end(c, bl_id, SC_GOSPEL, 0);
            }
        }
        SC_IMPOSITIO => {
            if get_sce(c, bl_id, SC_IMPOSITIO).map(|e| e.val1 > val1).unwrap_or(false) {
                status_change_end(c, bl_id, SC_IMPOSITIO, 0);
            }
        }
        SC_ENDURE => {
            if is_pc && data::sd_get(c, bl_id).map(|sd| sd.special_state.no_walk_delay).unwrap_or(false) {
                return true;
            }
        }
        SC_MADOGEAR => {
            let has = |x| has_sce(c, bl_id, x);
            for rem in status_db().remove_by_status_flag(&has, &[SCF_MADOCANCEL]) {
                status_change_end(c, bl_id, rem, 0);
            }
            if is_pc {
                super::bonus::pc_bonus_script_clear(c, bl_id, BSF_REM_ON_MADOGEAR);
            }
        }
        _ => {}
    }
    // Check for overlapping fails
    if let Some(sce) = get_sce(c, bl_id, t) {
        match t {
            SC_MERC_FLEEUP | SC_MERC_ATKUP | SC_MERC_HPUP | SC_MERC_SPUP | SC_MERC_HITUP => {
                if sce.val1 > val1 {
                    val1 = sce.val1;
                }
            }
            SC_ADRENALINE | SC_ADRENALINE2 | SC_WEAPONPERFECTION | SC_OVERTHRUST => {
                if sce.val2 > val2 {
                    return false;
                }
            }
            SC_GOSPEL => {
                if sce.val4 == BCT_SELF {
                    return false;
                }
                if sce.val1 > val1 {
                    return true;
                }
            }
            SC_ENDURE => {
                if sce.val4 != 0 && val4 == 0 {
                    return true; // Don't let you override infinite endure.
                }
                if sce.val1 > val1 {
                    return true;
                }
            }
            SC_JAILED => {
                // When a player is already jailed, do not edit the jail data.
                val2 = sce.val2;
                val3 = sce.val3;
                val4 = sce.val4;
            }
            SC_SHAPESHIFT | SC_PROPERTYWALK => {}
            SC_LEADERSHIP | SC_GLORYWOUNDS | SC_SOULCOLD | SC_HAWKEYES => {
                if sce.val4 != 0 && val4 == 0 {
                    return false;
                }
            }
            _ => {
                if t == SC_JOINTBEAT {
                    if sce.val2 & BREAK_NECK != 0 {
                        return false; // BREAK_NECK cannot be stacked with new breaks until the status is over.
                    }
                    val2 |= sce.val2; // Stackable ailments
                }
                if !scdb.has_flag(SCF_OVERLAPIGNORELEVEL) && sce.val1 > val1 {
                    return true; // Return true to not mess up skill animations. [Skotlex]
                }
            }
        }
    }
    let mut calc_flag = scdb.calc_flag;
    let mut tick_time: i32 = 0;
    if flag & SCSTART_LOADED == 0 {
        // Value setup per status change (&4 - Do not parse val settings when loading SCs)
        (st.val1, st.val2, st.val3, st.val4, st.tick, st.tick_time, st.flag) = (val1, val2, val3, val4, tick, tick_time, flag);
        st.bl = unit::get(c, bl_id).unwrap_or(bl.clone());
        if let super::sc_st::Flow::Return(r) = start_vals(&mut st) {
            return r;
        }
        (val1, val2, val3, val4, tick, tick_time) = (st.val1, st.val2, st.val3, st.val4, st.tick, st.tick_time);
    } else {
        // Special considerations when loading SC data.
        match t {
            SC_WEDDING | SC_XMAS | SC_SUMMER | SC_HANBOK | SC_OKTOBERFEST | SC_DRESSUP => {
                // (a loaded status is always a player's: its view data is the unit row's)
                if let Some(u) = unit::get(c, bl_id).filter(|u| u.kind == BL_PC) {
                    use crate::pc_equip::{clif_changelook, LOOK_BASE, LOOK_BODY2, LOOK_CLOTHES_COLOR, LOOK_SHIELD, LOOK_WEAPON};
                    clif_changelook(c, bl_id, LOOK_BASE, u.vd.class);
                    clif_changelook(c, bl_id, LOOK_WEAPON, 0);
                    clif_changelook(c, bl_id, LOOK_SHIELD, 0);
                    clif_changelook(c, bl_id, LOOK_CLOTHES_COLOR, u.vd.clothes_color);
                    clif_changelook(c, bl_id, LOOK_BODY2, u.vd.body2);
                }
            }
            SC_STONE | SC_STONEWAIT | SC_POISON | SC_DPOISON | SC_BLEEDING | SC_BURNING | SC_TOXIN | SC_SERVANTWEAPON | SC_ABYSSFORCEWEAPON => {
                tick_time = tick;
                tick = tick_time + val4.max(0);
            }
            SC_DEATHHURT => {
                if val3 != 1 {
                    tick_time = tick;
                    tick = tick_time + val4.max(0);
                    // [[fallthrough]] into the SC_MAGICMUSHROOM case
                    if val3 != 0 {
                        tick_time = tick;
                        tick = tick_time + val4.max(0);
                    }
                }
            }
            SC_MAGICMUSHROOM | SC_PYREXIA | SC_LEECHESEND => {
                if val3 != 0 {
                    tick_time = tick;
                    tick = tick_time + val4.max(0);
                }
            }
            SC_SWORDCLAN | SC_ARCWANDCLAN | SC_GOLDENMACECLAN | SC_CROSSBOWCLAN | SC_JUMPINGCLAN | SC_CLAN_INFO => {
                if is_pc && crate::pc::character_of(c, bl_id).map(|ch| ch.clan_id == 0).unwrap_or(false) {
                    return false;
                }
            }
            _ => {}
        }
    }
    if is_pc && super::calc::current_equip_combo_pos() > 0 && tick as i64 == INFINITE_TICK {
        log::warn!("sc_start: Item combo contains an INFINITE_TICK duration. Skipping bonus.");
        return false;
    }
    /* [Ind] */
    if scdb.has_flag(SCF_DISPLAYPC) || scdb.has_flag(SCF_DISPLAYNPC) {
        let (dval1, dval2, dval3) = match t {
            SC_ALL_RIDING => (1, 0, 0),
            SC_CLAN_INFO => (val1, val2, val3),
            _ => (val1, 0, 0),
        };
        status_display_add(c, &bl, t, dval1, dval2, dval3);
    }
    //SC that force player to stand if is sitting
    if scdb.has_flag(SCF_SETSTAND) && is_pc && crate::unit::get(c, bl_id).map(|u| u.vd.dead_sit == 2).unwrap_or(false) {
        crate::pc::setstand(c, bl_id, true);
    }
    //SC that make stop attacking [LuzZza]
    if scdb.has_flag(SCF_STOPATTACKING) {
        if let Some(mut u) = unit::get(c, bl_id) {
            unit::stop_attack(c, &mut u);
            unit::save(c, &u);
        }
    }
    //SC that make stop walking
    if scdb.has_flag(SCF_STOPWALKING) {
        stop_walking_for(c, bl_id, t);
    }
    //SC that make stop casting
    if crate::conf::sc_castcancel() & bl.kind != 0 && scdb.has_flag(SCF_STOPCASTING) {
        crate::skill::unit::unit_skillcastcancel(c, bl_id, 0);
    }
    let Some(mut u) = unit::get(c, bl_id) else { return false };
    if scdb.opt1 as i16 != OPT1_NONE {
        u.sc.opt1 = scdb.opt1 as i16;
    }
    u.sc.opt2 |= scdb.opt2 as i16;
    u.sc.opt3 |= scdb.opt3 as i32;
    u.sc.option |= scdb.look as i32;
    if t == SC_DANCING && (val1 & 0xFFFF) == crate::db::skills::CG_MOONLIT as i32 {
        u.sc.opt3 |= OPT3_MOONLIT;
    }
    unit::save(c, &u);
    let opt_flag = |f: u32| scdb.has_flag(f);
    let disable_opt_flag = false;
    // On Aegis, when turning on a status change, first goes the option packet, then the sc packet.
    if !disable_opt_flag && (opt_flag(SCF_SENDOPTION) || opt_flag(SCF_ONTOUCH) || opt_flag(SCF_UNITMOVE) || opt_flag(SCF_NONPLAYER) || opt_flag(SCF_SENDLOOK)) {
        crate::clif::changeoption(c, &u, u.sc.option, None);
        if is_pc && opt_flag(SCF_SENDLOOK) {
            use crate::pc_equip::{clif_changelook, LOOK_BASE, LOOK_CLOTHES_COLOR, LOOK_SHIELD, LOOK_WEAPON};
            clif_changelook(c, bl_id, LOOK_BASE, u.vd.class);
            clif_changelook(c, bl_id, LOOK_WEAPON, 0);
            clif_changelook(c, bl_id, LOOK_SHIELD, 0);
            let clothes = unit::get(c, bl_id).map(|u| u.vd.clothes_color).unwrap_or(0);
            clif_changelook(c, bl_id, LOOK_CLOTHES_COLOR, clothes);
        }
    }
    if calc_flag & (1u64 << SCB_DYE) != 0 {
        // Reset DYE color
        if u.vd.clothes_color != 0 {
            val4 = u.vd.clothes_color;
            super::clif::changelook_clothes_color(c, &u, 0);
        }
        calc_flag &= !(1u64 << SCB_DYE);
    }
    if flag & SCSTART_NOICON == 0 && !((flag & SCSTART_LOADED != 0 && scdb.has_flag(SCF_DISPLAYPC)) || scdb.has_flag(SCF_DISPLAYNPC)) {
        let status_icon = scdb.icon;
        super::clif::status_change(
            c,
            &u,
            status_icon,
            1,
            tick as i64,
            if scdb.has_flag(SCF_SENDVAL1) { val1 } else { 1 },
            if scdb.has_flag(SCF_SENDVAL2) { val2 } else { 0 },
            if scdb.has_flag(SCF_SENDVAL3) { val3 } else { 0 },
        );
    }
    // Used as temporary storage for scs with interval ticks, so that the actual duration is sent to the client first.
    if tick_time != 0 {
        tick = tick_time;
    }
    let existing = get_sce(c, bl_id, t);
    let sc_isnew = existing.is_none();
    let mut sce = match existing {
        Some(e) => {
            if e.timer != 0 {
                timer::delete(c, e.timer);
            }
            e
        }
        None => {
            // sc->createSCE(type)
            let mut us = unit_sc(c, bl_id);
            super::sc_umap::insert(&mut us, t);
            put_unit_sc(c, &us);
            c.db().sc_entry().insert(ScEntry { id: 0, bl_id, sc_type: t, timer: 0, val1: 0, val2: 0, val3: 0, val4: 0 })
        }
    };
    sce.val1 = val1;
    sce.val2 = val2;
    sce.val3 = val3;
    sce.val4 = val4;
    sce.timer = if tick >= 0 { timer::add(c, c.tick() + tick as i64, Func::StatusChangeTimer, bl_id, t as i64) } else { 0 };
    put_sce(c, &sce);
    if calc_flag != 0 {
        if is_pc {
            match t {
                SC_BERSERK | SC_MERC_HPUP | SC_MERC_SPUP | SC_EXTREMITYFIST => super::calc_bl(c, bl_id, calc_flag, SCO_FORCE),
                _ => {
                    if !crate::pc::get(c, bl_id).map(|p| p.connect_new).unwrap_or(false) {
                        super::calc_bl(c, bl_id, calc_flag, SCO_NONE);
                    }
                }
            }
        } else {
            super::calc_bl(c, bl_id, calc_flag, SCO_NONE);
        }
    }
    // Non-zero
    if sc_isnew && scdb.state != 0 {
        status_calc_state(c, bl_id, &scdb, true);
    }
    // Skotlex: Pet Status Effect Healing (status.cpp:13243-13244, pet.rs)
    if bl.kind == crate::map::BL_PC && crate::pet::data::of_master(c, bl_id).is_some() {
        crate::pet::pet_sc_check(c, bl_id, t);
    }
    // 1st thing to execute when loading status
    let status = super::status_get_status_data(c, &bl);
    match t {
        SC_BERSERK => {
            if sce.val2 == 0 {
                super::hp::status_heal(c, bl_id, status.max_hp as i64, 0, 0, 1);
                super::hp::status_set_sp(c, bl_id, 0, 0);
            }
            if let Some(mut e) = get_sce(c, bl_id, t) {
                e.val2 = (5u32.wrapping_mul(status.max_hp) / 100) as i32; // (uint32 arithmetic)
                put_sce(c, &e);
            }
        }
        SC_FULL_THROTTLE | SC_MERC_HPUP => {
            super::hp::status_percent_heal(c, bl_id, 100, 0, 0);
        }
        SC_MERC_SPUP => {
            super::hp::status_percent_heal(c, bl_id, 0, 100, 0);
        }
        SC_ITEMSCRIPT => {
            // Shows Buff Icons
            if is_pc {
                if let Some(u) = unit::get(c, bl_id) {
                    super::clif::status_change(c, &u, val2, 1, tick as i64, 0, 0, 0);
                }
            }
        }
        SC_GVG_GIANT | SC_GVG_GOLEM | SC_GVG_STUN | SC_GVG_STONE | SC_GVG_FREEZ | SC_GVG_SLEEP | SC_GVG_CURSE | SC_GVG_SILENCE | SC_GVG_BLIND => {
            if val1 != 0 || val2 != 0 {
                super::hp::status_zap(c, bl_id, val1 as i64, val2 as i64, 0);
            }
        }
        SC_RUN | SC_WUGDASH => {
            // ud->state.running = unit_run(bl, t == SC_RUN ? nullptr : sd, type)
            let running = super::sc_ext::unit_run(c, bl_id, t == SC_WUGDASH && is_pc, t);
            if let Some(mut u) = unit::get(c, bl_id) {
                u.ud.running = running;
                unit::save(c, &u);
            }
        }
        SC_BOSSMAPINFO => {
            use super::sc_ext::*;
            if !is_pc {
                return false;
            }
            let Some(boss_md) = map_id2boss(c, sce.val1) else { return false };
            let sd = unit::get(c, bl_id).unwrap_or(bl.clone());
            let boss_u = unit::get(c, boss_md.id);
            if boss_u.as_ref().map(|u| u.map) != Some(sd.map) {
                // Not on same map anymore
                return false;
            } else if boss_u.as_ref().map(|u| u.on_map).unwrap_or(false) {
                // Boss is alive
                if let Some(mut e) = get_sce(c, bl_id, t) {
                    e.val2 = 0;
                    put_sce(c, &e);
                }
                clif_bossmapinfo(c, &sd, Some(&boss_md), BOSS_INFO_ALIVE_WITHMSG);
            } else if boss_md.spawn_timer != 0 {
                // Boss is dead
                if let Some(mut e) = get_sce(c, bl_id, t) {
                    e.val2 = 1;
                    put_sce(c, &e);
                }
                clif_bossmapinfo(c, &sd, Some(&boss_md), BOSS_INFO_DEAD);
            }
        }
        SC_COMBO => {
            use crate::db::skills::ids::*;
            use crate::db::skills::{INF_PASSIVE_SKILL, INF_SELF_SKILL};
            use crate::skill::clif::clif_skill_nodamage;
            use crate::skill::combo::skill_combo_toggle_inf;
            if let Some(u) = unit::get(c, bl_id) {
                let ready = match sce.val1 as u16 {
                    TK_STORMKICK => Some(TK_READYSTORM),
                    TK_DOWNKICK => Some(TK_READYDOWN),
                    TK_TURNKICK => Some(TK_READYTURN),
                    TK_COUNTER => Some(TK_READYCOUNTER),
                    _ => None,
                };
                match ready {
                    Some(r) => {
                        skill_combo_toggle_inf(c, &u, TK_JUMPKICK, INF_PASSIVE_SKILL as i32);
                        clif_skill_nodamage(c, Some(&u), &u, r, 1, true);
                    }
                    // Rest just toggle inf to enable autotarget
                    None => skill_combo_toggle_inf(c, &u, sce.val1 as u16, INF_SELF_SKILL as i32),
                }
            }
        }
        SC_C_MARKER => {
            //Send mini-map, don't wait for first timer triggered
            if let Some(s) = src.and_then(|s| unit::get(c, s)).filter(|s| s.kind == BL_PC) {
                if let Some(u) = unit::get(c, bl_id) {
                    super::sc_ext::clif_crimson_marker(c, &s, &u, false);
                }
            }
        }
        _ => {}
    }
    let _ = src;
    if opt_flag(SCF_ONTOUCH) && is_pc {
        crate::script::npc_run::npc_touchnext_areanpc(c, bl_id, false);
    }
    true
}

/// status_change_start_post_delay's value setup switch (`switch (type)` under `if (!(flag&SCSTART_LOADED))`): the
/// cases are split over sc_vals_a.rs (status.cpp permanent effects .. SC_INVINCIBLE), sc_vals_b.rs (SC_FEAR ..
/// SC_ACTIVE_MONSTER_TRANSFORM) and sc_vals_c.rs (SC_EPICLESIS .. SC_OVERCOMING_CRISIS), in rAthena's order; a
/// status none of them handles takes the `default:` case here.
fn start_vals(st: &mut super::sc_st::StartSt) -> super::sc_st::Flow {
    use super::sc_st::Flow;
    if let Some(f) = super::sc_vals_a::vals(st) {
        return f;
    }
    if let Some(f) = super::sc_vals_b::vals(st) {
        return f;
    }
    if let Some(f) = super::sc_vals_c::vals(st) {
        return f;
    }
    let scdb = &st.scdb;
    if scdb.calc_flag == 0
        && scdb.skill_id == 0
        && scdb.icon == crate::db::misc::status::EFST_BLANK
        && scdb.opt1 == 0
        && scdb.opt2 == 0
        && scdb.state == 0
        && scdb.flag == 0
        && scdb.endonstart.is_empty()
        && scdb.endreturn.is_empty()
        && scdb.fail.is_empty()
        && scdb.endonend.is_empty()
    {
        // Status change with no calc, no icon, and no skill associated...?
        log::warn!("status_change_start: Status {} is bare. Add the NoWarning flag to suppress this message.", st.t);
        return Flow::Return(false);
    }
    Flow::Continue
}

/// RENEWAL status_get_sc_def: `uint16 levelAdv = (static_cast<uint16>(pow(max(0, lv(src) - lv(bl)), 2)) / 5) * 100`
/// (status.cpp:9658). The product is computed in int and stored in a uint16, so it wraps (a difference of 58 levels
/// gives 1664); the cast of a square above 65535 (256 levels and more) keeps the low 16 bits of the int conversion, as
/// gcc's code does.
pub fn level_adv(lv_src: i32, lv_bl: i32) -> i32 {
    let square = (0.max(lv_src - lv_bl) as f64).powi(2) as i32 as u16;
    ((square as i32 / 5) * 100) as u16 as i32
}

/// The SCF_STOPWALKING part of status_change_start (status.cpp:13118-13143).
fn stop_walking_for(c: &Ctx, bl_id: u32, t: i32) {
    let Some(mut u) = unit::get(c, bl_id) else { return };
    match t {
        SC__MANHOLE => {
            if u.kind == BL_PC || unit_blown_immune(c, &u, 0x1) == UB_KNOCKABLE {
                unit::stop_walking(c, &mut u, unit::USW_FIXPOS, 0);
                unit::save(c, &u);
            }
        }
        SC_VACUUM_EXTREME => {
            if u.kind != BL_PC && unit_blown_immune(c, &u, 0x1) == UB_KNOCKABLE {
                unit::stop_walking(c, &mut u, unit::USW_FIXPOS, 0);
                unit::stop_attack(c, &mut u);
                unit::save(c, &u);
            }
        }
        SC_FREEZE | SC_STUN | SC_STONE => {
            if has_sce(c, bl_id, SC_DANCING) {
                unit::stop_walking(c, &mut u, unit::USW_FIXPOS, 0);
                unit::save(c, &u);
                status_change_end(c, bl_id, SC_DANCING, 0);
            }
        }
        _ => {
            if unit_blown_immune(c, &u, 0x1) == UB_KNOCKABLE {
                unit::stop_walking(c, &mut u, unit::USW_FIXPOS, 0);
                unit::save(c, &u);
            }
        }
    }
}

// unit.cpp unit_blown_immune: the battle agent's port (monsters' MD_KNOCKBACKIMMUNE included), which replaces the fix
// agent's local shim for players and maps.
use crate::battle::knockback::{unit_blown_immune, UB_KNOCKABLE};

// ---------------------------------------------------------------------------------------------- end

/// status.cpp status_change_end. `tid` 0 = INVALID_TIMER (ended by code, not by its timer).
pub fn status_change_end(c: &Ctx, bl_id: u32, t: i32, tid: u64) -> i32 {
    let Some(mut sce) = get_sce(c, bl_id, t) else { return 0 };
    if sce.timer != tid && tid != 0 {
        return 0;
    }
    if tid == 0 {
        if t == SC_ENDURE && sce.val4 != 0 {
            // Do not end infinite endure.
            return 0;
        }
        if t == SC_SPIDERWEB {
            //Delete the unit group first to expire found in the status change
            use crate::skillunit::{skill_delunitgroup, skill_id2group};
            let tick = c.tick();
            let left = |g: &crate::skillunit::SkillUnitGroup| g.limit - (tick - g.tick);
            let mut group = None;
            let mut pos = 1;
            if sce.val2 != 0 {
                group = skill_id2group(c, sce.val2);
                if group.is_none() {
                    sce.val2 = 0;
                }
            }
            if sce.val3 != 0 {
                match skill_id2group(c, sce.val3) {
                    None => sce.val3 = 0,
                    Some(g2) => {
                        if group.as_ref().map(|g| left(g) > left(&g2)).unwrap_or(true) {
                            group = Some(g2);
                            pos = 2;
                        }
                    }
                }
            }
            if sce.val4 != 0 {
                match skill_id2group(c, sce.val4) {
                    None => sce.val4 = 0,
                    Some(g2) => {
                        if group.as_ref().map(|g| left(g) > left(&g2)).unwrap_or(true) {
                            group = Some(g2);
                            pos = 3;
                        }
                    }
                }
            }
            match pos {
                1 => sce.val2 = 0,
                2 => sce.val3 = 0,
                _ => sce.val4 = 0,
            }
            put_sce(c, &sce);
            if let Some(g) = group {
                skill_delunitgroup(c, g.group_id);
            }
            if let Some(e) = get_sce(c, bl_id, t) {
                sce = e;
            }
            let dead = unit::get(c, bl_id).map(|u| status_isdead(c, &u)).unwrap_or(true);
            if !dead && (sce.val2 != 0 || sce.val3 != 0 || sce.val4 != 0) {
                return 0; //Don't end the status change yet as there are still unit groups associated with it
            }
        }
    }
    let (val1, _val2, val3, val4) = (sce.val1, sce.val2, sce.val3, sce.val4);
    if sce.timer != 0 && sce.timer != tid {
        timer::delete(c, sce.timer);
    }
    delete_sce(c, bl_id, &sce);
    let Some(scdb) = status_db().find(t).cloned() else { return 0 };
    if scdb.state != 0 {
        status_calc_state(c, bl_id, &scdb, false);
    }
    let Some(bl) = unit::get(c, bl_id) else { return 1 };
    if scdb.has_flag(SCF_DISPLAYPC) || scdb.has_flag(SCF_DISPLAYNPC) {
        status_display_remove(c, &bl, t);
    }
    let is_pc = bl.kind == BL_PC;
    let mut est = super::sc_st::EndSt { c, bl: bl.clone(), t, tid, val1, val2: _val2, val3, val4, calc_flag: scdb.calc_flag, scdb: scdb.clone() };
    if let super::sc_st::EndFlow::Return(r) = super::sc_end_fx::end_effect(&mut est) {
        return r;
    }
    let (val1, _val2, val3, val4) = (est.val1, est.val2, est.val3, est.val4);
    let mut calc_flag = est.calc_flag;
    // End statuses found in the EndOnEnd list.
    for &e in &scdb.endonend {
        status_change_end(c, bl_id, e, 0);
    }
    // Reset the options as needed
    let opt_flag = |f: u32| scdb.has_flag(f);
    let mut disable_opt_flag = false;
    let Some(mut u) = unit::get(c, bl_id) else { return 1 };
    match t {
        SC_DANCING => {
            if (val1 & 0xFFFF) == crate::db::skills::CG_MOONLIT as i32 {
                u.sc.opt3 &= !OPT3_MOONLIT;
            }
        }
        SC_INCATKRATE => {
            if bl.kind != BL_MOB {
                disable_opt_flag = true;
            }
        }
        _ => {}
    }
    if scdb.opt1 != 0 {
        u.sc.opt1 = OPT1_NONE;
    }
    if scdb.opt2 != 0 {
        u.sc.opt2 &= !(scdb.opt2 as i16);
    }
    if scdb.opt3 != 0 {
        u.sc.opt3 &= !(scdb.opt3 as i32);
    }
    if scdb.look != 0 {
        u.sc.option &= !(scdb.look as i32);
    }
    unit::save(c, &u);
    if calc_flag & (1u64 << SCB_DYE) != 0 {
        // Restore DYE color
        if u.vd.clothes_color == 0 && val4 != 0 {
            super::clif::changelook_clothes_color(c, &u, val4);
        }
        calc_flag &= !(1u64 << SCB_DYE);
    }
    // On Aegis, when turning off a status change, first goes the sc packet, then the option packet.
    super::clif::status_change(c, &u, scdb.icon, 0, 0, 0, 0, 0);
    if opt_flag(SCF_NONPLAYER) {
        // bugreport:681
        if let Some(u) = unit::get(c, bl_id) {
            super::sc_ext::clif_changeoption2(c, &u);
        }
    } else if !disable_opt_flag && (opt_flag(SCF_SENDOPTION) || opt_flag(SCF_ONTOUCH) || opt_flag(SCF_UNITMOVE) || opt_flag(SCF_NONPLAYER) || opt_flag(SCF_SENDLOOK)) {
        crate::clif::changeoption(c, &u, u.sc.option, None);
        if is_pc && opt_flag(SCF_SENDLOOK) {
            use crate::pc_equip::{clif_changelook, LOOK_BASE, LOOK_BODY2, LOOK_CLOTHES_COLOR, LOOK_SHIELD, LOOK_WEAPON};
            let class = unit::get(c, bl_id).map(|u| u.vd.class).unwrap_or(0);
            clif_changelook(c, bl_id, LOOK_BASE, class);
            // sd->update_look(LOOK_WEAPON / LOOK_SHIELD) happen inside clif_changelook(LOOK_WEAPON / LOOK_SHIELD)
            let (weapon, shield) = unit::get(c, bl_id).map(|u| (u.vd.weapon, u.vd.shield)).unwrap_or((0, 0));
            clif_changelook(c, bl_id, LOOK_WEAPON, weapon);
            clif_changelook(c, bl_id, LOOK_SHIELD, shield);
            let ch = crate::pc::character_of(c, bl_id);
            clif_changelook(c, bl_id, LOOK_CLOTHES_COLOR, cap_value(ch.as_ref().map(|ch| ch.clothes_color as i32).unwrap_or(0), 0, super::sc_conf::max_cloth_color()));
            clif_changelook(c, bl_id, LOOK_BODY2, ch.as_ref().map(|ch| ch.body as i32).unwrap_or(0));
        }
    }
    if calc_flag != 0 {
        super::calc_bl(c, bl_id, calc_flag, SCO_NONE);
    }
    if opt_flag(SCF_UNITMOVE) {
        // Out of hiding, invoke on place.
        crate::skillunit::skill_unit_move_id(c, bl_id, c.tick(), 1);
    }
    if opt_flag(SCF_ONTOUCH) && is_pc && !crate::pc::get(c, bl_id).map(|p| p.warping).unwrap_or(true) {
        if let Some(u) = unit::get(c, bl_id) {
            if crate::map::getcell(c, u.map, u.x, u.y, crate::map::CellChk::ChkNpc) {
                crate::npc::touch_area_allnpc(c, bl_id, u.map, u.x, u.y);
            }
        }
    }
    // Needed to be here to make sure OPT1_STONEWAIT has been cleared from the target
    if t == SC_STONEWAIT && tid != 0 {
        if let Some(u) = unit::get(c, bl_id) {
            status_change_start(c, Some(&u), &u, SC_STONE, 100, val1, _val2, 0, 0, val3 as i64, SCSTART_NOAVOID, 0);
        }
    }
    1
}

/// status.cpp status_get_sc_interval.
pub(crate) fn status_get_sc_interval(t: i32) -> i32 {
    match t {
        SC_POISON | SC_LEECHESEND | SC_DPOISON | SC_DEATHHURT | SC_GRADUAL_GRAVITY | SC_KILLING_AURA | SC_BOSSMAPINFO => 1000,
        SC_WINKCHARM | SC_VOICEOFSIREN => 2250,
        SC_BURNING | SC_PYREXIA => 3000,
        SC_MAGICMUSHROOM => 4000,
        SC_STONE => 5000,
        SC_BLEEDING | SC_TOXIN => 10000,
        SC_HELLS_PLANT => 333,
        SC_SHIELDSPELL_HP => 3000,
        SC_SHIELDSPELL_SP => 5000,
        SC_STAR_BURST => 300,
        _ => 0,
    }
}

/// status.cpp status_change_timer (timer data = sc_type). The per-status cases are sc_timer_fx.rs.
pub fn status_change_timer(c: &Ctx, tid: u64, tick: Tick, bl_id: u32, t: i32) {
    let interval = status_get_sc_interval(t);
    let Some(bl) = unit::get(c, bl_id) else {
        log::debug!("status_change_timer: Null pointer id: {bl_id} data: {t}");
        return;
    };
    let Some(sce) = get_sce(c, bl_id, t) else {
        log::debug!("status_change_timer: Null pointer id: {bl_id} data: {t} bl-type: {}", bl.kind);
        return;
    };
    if sce.timer != tid {
        log::error!("status_change_timer: Mismatch for type {t}: {tid} != {} (bl id {bl_id})", sce.timer);
        return;
    }
    let mut st = super::sc_st::TimerSt { c, tid, tick, bl, t, sce, interval };
    if super::sc_timer_fx::timer_effect(&mut st) == super::sc_st::TimerFlow::Return {
        return;
    }
    // If status has an interval and there is at least 100ms remaining time, wait for next interval
    if st.interval > 0 && has_sce(c, bl_id, t) && st.sce.val4 >= 100 {
        let due = st.sce.val4.min(st.interval) as i64 + tick;
        st.next(due);
        st.sce.val4 -= st.interval;
        st.sync();
        return;
    }
    // Default for all non-handled control paths is to end the status
    status_change_end(c, bl_id, t, tid);
}

/// status.cpp status_change_clear.
/// type 0: PC killed; 1: quitting (a status that stays is deleted anyway); 2: do clif_changeoption();
/// 3: do not remove some permanent/time-independent effects.
pub fn status_change_clear(c: &Ctx, bl_id: u32, kind: i32) -> i32 {
    let mut us = unit_sc(c, bl_id);
    us.comet_x = 0;
    us.comet_y = 0;
    put_unit_sc(c, &us);
    if load(c, bl_id).empty() {
        return 0;
    }
    for s in status_db().iter() {
        let status = s.type_;
        let Some(sce) = get_sce(c, bl_id, status) else { continue };
        if kind == 0 && s.has_flag(SCF_NOREMOVEONDEAD) {
            // Only when its Holy or Dark that it doesn't dispell on death
            if !(status == SC_ELEMENTALCHANGE && sce.val2 != ELE_HOLY && sce.val2 != ELE_DARK) {
                continue;
            }
        }
        if kind == 3 && s.has_flag(SCF_NOCLEARBUFF) {
            continue;
        }
        status_change_end(c, bl_id, status, 0);
        if kind == 1 {
            if let Some(e) = get_sce(c, bl_id, status) {
                // If for some reason status_change_end decides to still keep the status when quitting. [Skotlex]
                if e.timer != 0 {
                    timer::delete(c, e.timer);
                }
                delete_sce(c, bl_id, &e);
            }
        }
    }
    if let Some(mut u) = unit::get(c, bl_id) {
        u.sc.opt1 = 0;
        u.sc.opt2 = 0;
        u.sc.opt3 = 0;
        unit::save(c, &u);
        if kind == 0 || kind == 2 {
            crate::clif::changeoption(c, &u, u.sc.option, None);
        }
    }
    1
}

// ---------------------------------------------------------------------------------------------- state

/// status.cpp status_calc_state_sub.
#[allow(clippy::type_complexity)]
fn calc_state_sub(
    c: &Ctx,
    bl: &Unit,
    sc: &Sc,
    start: bool,
    scdb_main: &StatusChangeDb,
    restriction: &mut bool,
    flag: u32,
    flag_conditional: u32,
    func_switch: &dyn Fn(&Ctx, &Unit, &Sc, &mut bool, i32, &ScEntry) -> bool,
) {
    // If starting and unconditional no further checks are needed
    if start && scdb_main.has_state(flag) {
        *restriction = true;
        return;
    }
    // Otherwise remove the restriction
    *restriction = false;
    // And check all remaining active status changes, if the restriction should still be active
    for e in &sc.list {
        let Some(other) = status_db().find(e.sc_type) else { continue };
        if other.has_state(flag) {
            *restriction = true;
            return;
        }
        if !other.has_state(flag_conditional) {
            continue;
        }
        if !func_switch(c, bl, sc, restriction, e.sc_type, e) {
            log::error!("status_calc_state_sub: status \"{}\" is defined with \"{}\", but the condition is not implemented.", e.sc_type, flag_conditional);
            continue;
        }
        if *restriction {
            return;
        }
    }
}

/// map.hpp manner_flags.
const MANNER_NOCHAT: i32 = 0x01;
const MANNER_NOITEM: i32 = 0x08;

/// status.cpp status_calc_state.
pub fn status_calc_state(c: &Ctx, bl_id: u32, scdb: &StatusChangeDb, start: bool) {
    let Some(bl) = unit::get(c, bl_id) else { return };
    let sc = load(c, bl_id);
    let mut us = unit_sc(c, bl_id);
    // No sc at all, we can zero without any extra weight over our conciousness
    if sc.empty() {
        us.cant = 0;
        put_unit_sc(c, &us);
        return;
    }
    let not_impl = |_: &Ctx, _: &Unit, _: &Sc, _: &mut bool, _: i32, _: &ScEntry| false;
    let apply = |us: &mut ScUnit, cant_flag: u32, cond: u32, f: &dyn Fn(&Ctx, &Unit, &Sc, &mut bool, i32, &ScEntry) -> bool| {
        if scdb.has_state(cant_flag) || scdb.has_state(cond) {
            let mut r = us.cant & cant_bit(cant_flag) != 0;
            calc_state_sub(c, &bl, &sc, start, scdb, &mut r, cant_flag, cond, f);
            if r {
                us.cant |= cant_bit(cant_flag);
            } else {
                us.cant &= !cant_bit(cant_flag);
            }
        }
    };
    // Can't move
    apply(&mut us, SCS_NOMOVE, SCS_NOMOVECOND, &|c, bl, sc, r, t, sce| {
        match t {
            SC_GOSPEL => {
                if sce.val4 == BCT_SELF {
                    *r = true;
                }
            }
            SC_CAMOUFLAGE => {
                if sce.val1 < 3 {
                    *r = true;
                }
            }
            SC_MAGNETICFIELD => {
                if sce.val2 != bl.id as i32 {
                    *r = true;
                }
            }
            SC_FEAR => {
                if sce.val2 > 0 {
                    *r = true;
                }
            }
            SC_HIDING => {
                if bl.kind != BL_PC || crate::skill::checkskill(c, bl.id, crate::db::skills::RG_TUNNELDRIVE) == 0 {
                    *r = true;
                }
            }
            SC_DANCING => {
                if sce.val4 != 0 {
                    // In Renewal the restiction only applies for CG_MOONLIT and CG_HERMODE
                    let s = sce.val1 & 0xFFFF;
                    if s == crate::db::skills::CG_MOONLIT as i32 || s == crate::db::skills::CG_HERMODE as i32 {
                        *r = true;
                    }
                }
            }
            SC_LONGING => {}
            SC_CRYSTALIZE => {
                if bl.kind != BL_MOB {
                    *r = true;
                }
            }
            _ => return false,
        }
        let _ = sc;
        true
    });
    // Can't use skills
    apply(&mut us, SCS_NOCAST, SCS_NOCASTCOND, &|_, bl, _, r, t, sce| {
        match t {
            SC_WINKCHARM => {
                if bl.kind != BL_PC {
                    *r = true;
                }
            }
            SC_OBLIVIONCURSE => {
                if sce.val3 == 1 {
                    *r = true;
                }
            }
            _ => return false,
        }
        true
    });
    // Can't chat
    apply(&mut us, SCS_NOCHAT, SCS_NOCHATCOND, &|_, _, _, r, t, sce| {
        match t {
            SC_NOCHAT => {
                if sce.val1 & MANNER_NOCHAT != 0 {
                    *r = true;
                }
            }
            _ => return false,
        }
        true
    });
    // Can't attack
    apply(&mut us, SCS_NOATTACK, SCS_NOATTACKCOND, &|_, _, sc, r, t, sce| {
        match t {
            SC_DANCING => {
                if sce.val4 == 0 || !sc.has(SC_LONGING) {
                    *r = true;
                }
            }
            SC_LONGING => {}
            _ => return false,
        }
        true
    });
    // Can't warp
    apply(&mut us, SCS_NOWARP, SCS_NOWARPCOND, &not_impl);
    // Player-only states
    if bl.kind == BL_PC {
        let noitem = |_: &Ctx, _: &Unit, _: &Sc, r: &mut bool, t: i32, sce: &ScEntry| -> bool {
            match t {
                SC_NOCHAT => {
                    if sce.val1 & MANNER_NOITEM != 0 {
                        *r = true;
                    }
                }
                _ => return false,
            }
            true
        };
        apply(&mut us, SCS_NOPICKITEM, SCS_NOPICKITEMCOND, &noitem);
        apply(&mut us, SCS_NODROPITEM, SCS_NODROPITEMCOND, &noitem);
        apply(&mut us, SCS_NOEQUIPITEM, SCS_NOEQUIPITEMCOND, &not_impl);
        apply(&mut us, SCS_NOUNEQUIPITEM, SCS_NOUNEQUIPITEMCOND, &not_impl);
        apply(&mut us, SCS_NOCONSUMEITEM, SCS_NOCONSUMEITEMCOND, &|_, _, _, r, t, sce| {
            match t {
                SC_GRAVITATION => {
                    if sce.val3 == BCT_SELF {
                        *r = true;
                    }
                }
                SC_NOCHAT => {
                    if sce.val1 & MANNER_NOITEM != 0 {
                        *r = true;
                    }
                }
                _ => return false,
            }
            true
        });
        apply(&mut us, SCS_NODEATHPENALTY, SCS_NODEATHPENALTYCOND, &not_impl);
        apply(&mut us, SCS_NOINTERACT, SCS_NOINTERACTCOND, &not_impl);
    }
    put_unit_sc(c, &us);
}

// ---------------------------------------------------------------------------------------------- display

/// status.cpp status_display_add (players and NPCs).
pub fn status_display_add(c: &Ctx, bl: &Unit, t: i32, dval1: i32, dval2: i32, dval3: i32) {
    if bl.kind != BL_PC && bl.kind != BL_NPC {
        return;
    }
    let mut us = unit_sc(c, bl.id);
    if let Some(e) = us.display.iter_mut().find(|d| d.sc_type == t) {
        e.val1 = dval1;
        e.val2 = dval2;
        e.val3 = dval3;
    } else {
        us.display.push(ScDisplay { sc_type: t, val1: dval1, val2: dval2, val3: dval3 });
    }
    put_unit_sc(c, &us);
}

/// status.cpp status_display_remove.
pub fn status_display_remove(c: &Ctx, bl: &Unit, t: i32) {
    if bl.kind != BL_PC && bl.kind != BL_NPC {
        return;
    }
    let mut us = unit_sc(c, bl.id);
    us.display.retain(|d| d.sc_type != t);
    put_unit_sc(c, &us);
}

/// status.cpp status_change_clear_onChangeMap (passes the map's raw zone).
pub fn status_change_clear_on_change_map(c: &Ctx, bl: &Unit) {
    use crate::npc::*;
    let sc = load(c, bl.id);
    if sc.empty() {
        return;
    }
    let m = bl.map;
    let map_is_vs = mapdata_flag_vs2(c, m);
    let map_is_pvp = mapflag(c, m, MF_PVP);
    let map_is_gvg = mapdata_flag_gvg2_no_te(c, m);
    let map_is_bg = mapflag(c, m, MF_BATTLEGROUND);
    let map_is_te = mapdata_flag_gvg2_te(c, m);
    let zone = mapflag_value(c, m, MF_RESTRICTED) as u32;
    for s in status_db().iter() {
        let t = s.type_;
        if !has_sce(c, bl.id, t) || status_db().sc_disabled(t) == 0 {
            continue;
        }
        if is_disabled_on_map_(t, map_is_vs, map_is_pvp, map_is_gvg, map_is_bg, zone, map_is_te) {
            status_change_end(c, bl.id, t, 0);
        }
    }
}

/// status.cpp status_clear_lastEffect_timer.
pub fn status_clear_last_effect_timer(c: &Ctx, bl_id: u32) {
    if unit::get(c, bl_id).is_none() {
        return;
    }
    let mut us = unit_sc(c, bl_id);
    us.last_effect = SC_NONE;
    us.last_effect_timer = 0;
    put_unit_sc(c, &us);
}

#[cfg(test)]
mod tests {
    /// status.cpp:9658 levelAdv wraps in its uint16.
    #[test]
    fn level_adv_wraps_in_uint16() {
        assert_eq!(super::level_adv(10, 20), 0);
        assert_eq!(super::level_adv(30, 20), 2000); // 100 / 5 * 100
        assert_eq!(super::level_adv(78, 20), 1664); // 3364 / 5 * 100 = 67200 -> 1664
        assert_eq!(super::level_adv(275, 1), 59728); // 75076 -> 9540 (low 16 bits), 1908 * 100 = 190800 -> 59728
    }
}
