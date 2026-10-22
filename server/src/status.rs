//! map/status.cpp and the status parts of pc.cpp / clif.cpp (rAthena e985006, RENEWAL): calculated status
//! (status_calc_pc and friends), item / card / combo bonuses (pc_bonus*), status changes (sc_start / sc_end and
//! their per-stat effects), HP / SP / AP bookkeeping and natural regen, character progression (stat points, EXP,
//! levels) and the status packets.
//!
//! Submodules: `data` (tables), `calc` (status_calc_pc / status_calc_bl_), `calc_sc` (per-stat status change
//! modifiers), `sc` (status change framework), `bonus` (pc_bonus*, autobonus, bonus_script), `hp` (heal / damage /
//! regen), `progress` (stats, EXP, levels), `clif` (packets), `scripts` (script sources), `consts` / `defs`.
//!
//! What other systems call:
//! - `calc_pc(c, aid, opt)` after anything that changes a character's status (equip, level, skills, …);
//!   `calc_bl(c, id, flags, opt)` for a partial recalculation (SCB_* bits).
//! - `hp::status_heal`, `hp::status_damage`, `hp::status_zap`, `hp::status_percent_heal`, `hp::status_set_hp`,
//!   `hp::status_revive`, `hp::pc_itemheal` (+ `hp::set_potion_flag`), `hp::pc_percentheal`.
//! - `sc::sc_start` / `sc::sc_start2` / `sc::sc_start4` / `sc::status_change_start`, `sc::status_change_end`,
//!   `sc::status_change_clear`.
//! - `progress::pc_gainexp` / `pc_lostexp`, `progress::pc_statusup2`, `progress::pc_skill`.
//! - `clif::updatestatus(c, aid, SP_*)`, `calc::status_calc_weight` / `status_calc_cart_weight`.
//!
//! The items system (inventory.rs `pc_items`) owns the inventory side of map_session_data: sd->weight / max_weight,
//! cart weight, equip_index, weapontype1 / 2, combos and itemid; this system owns sd->add_max_weight and computes
//! max_weight (status_calc_weight).

pub mod bonus;
pub mod calc;
pub mod calc_sc;
pub mod check;
pub mod clif;
pub mod consts;
pub mod data;
pub mod defs;
pub mod hp;
pub mod param;
pub mod progress;
pub mod sc;
#[cfg(test)]
mod sc_cases;
pub mod sc_clear;
pub mod sc_conf;
pub mod sc_end_fx;
pub mod sc_ext;
pub mod sc_immune;
pub mod sc_st;
pub mod sc_timer_fx;
pub mod sc_umap;
pub mod sc_vals_a;
pub mod sc_vals_b;
pub mod sc_vals_c;
pub mod scripts;

use spacetimedb::Table;

use crate::character::{character, Character};
use crate::unit::{self, Unit};
use crate::world::Ctx;
use consts::*;
use data::{PcRegenTick, PcStatus, StatusData};
pub use defs::{SCO_FIRST, SCO_FORCE, SCO_NONE};

/// Default player walking speed (136 ms per cell: 10% faster than rAthena's stock 150 ms).
pub const DEFAULT_PC_WALK_SPEED: u16 = 136;

thread_local! {
    static IN_PC_BASE_CALC: std::cell::Cell<u32> = const { std::cell::Cell::new(0) };
}

pub fn reset_in_pc_base_calc() {
    IN_PC_BASE_CALC.with(|x| x.set(0));
}

/// status.hpp status_calc_pc: status_calc_bl_(sd, SCB_ALL, opt).
pub fn calc_pc(c: &Ctx, aid: u32, opt: u8) {
    calc_bl(c, aid, crate::db::misc::status::status_db().get_scb_all(), opt);
}

/// status.cpp status_calc_bl_: recalculates the SCB_* parts in `flag`.
pub fn calc_bl(c: &Ctx, bl: u32, flag: u64, opt: u8) {
    if flag & (1u64 << SCB_BASE) != 0 && unit::get(c, bl).map(|u| u.kind == defs::BL_PC).unwrap_or(false) {
        if IN_PC_BASE_CALC.with(|x| x.get()) == bl {
            calc::calc_bl(c, bl, flag & !(1u64 << SCB_SPEED), opt);
            return;
        }
        let held = data::hold(c, bl);
        if !data::is_held(bl) {
            return;
        }
        if let Some(mut sd) = data::sd_get(c, bl) {
            if sd.delayed_damage != 0 && opt & SCO_FORCE == 0 {
                calc::calc_bl(c, bl, flag, opt);
            } else if !sd.permanent_speed {
                let old_battle_speed = sd.battle_status.speed;
                sd.base_status.speed = old_battle_speed;
                sd.permanent_speed = true;
                data::sd_put(c, &sd);
                IN_PC_BASE_CALC.with(|x| x.set(bl));
                calc::calc_bl(c, bl, flag & !(1u64 << SCB_SPEED), opt);
                IN_PC_BASE_CALC.with(|x| x.set(0));
                if let Some(mut sd) = data::sd_get(c, bl) {
                    if !sc::has_sce(c, bl, SC_WALKSPEED) {
                        sd.permanent_speed = false;
                        sd.base_status.speed = DEFAULT_PC_WALK_SPEED;
                        sd.battle_status.speed = old_battle_speed;
                        data::sd_put(c, &sd);
                    }
                }
                if flag & (1u64 << SCB_SPEED) != 0 {
                    calc::calc_bl(c, bl, 1u64 << SCB_SPEED, SCO_NONE);
                }
            } else {
                calc::calc_bl(c, bl, flag, opt);
            }
        }
        if held {
            data::release(c, bl);
            calc::sync_unit(c, bl);
        }
        return;
    }
    if flag & (1u64 << SCB_SPEED) != 0 {
        if let Some(mut sd) = data::sd_get(c, bl) {
            if !sd.permanent_speed && sd.base_status.speed == defs::DEFAULT_WALK_SPEED as u16 {
                sd.base_status.speed = DEFAULT_PC_WALK_SPEED;
                data::sd_put(c, &sd);
            }
        }
    }
    calc::calc_bl(c, bl, flag, opt);
}

// ---------------------------------------------------------------------------------------------- accessors

/// status.cpp dummy_status (initDummyData).
pub(crate) fn dummy_status() -> StatusData {
    StatusData {
        hp: 1,
        max_hp: 1,
        max_sp: 1,
        str: 1,
        agi: 1,
        vit: 1,
        int_: 1,
        dex: 1,
        luk: 1,
        hit: 1,
        speed: 2000,
        adelay: 4000,
        amotion: 2000,
        dmotion: 2000,
        ele_lv: 1,
        mode: defs::MD_CANMOVE,
        ..Default::default()
    }
}

/// A player's (or a monster's: md->status) battle status (None for other units and players not in the world).
pub fn status_data_of(c: &Ctx, bl: u32) -> Option<StatusData> {
    match data::sd_get(c, bl) {
        Some(sd) => Some(sd.battle_status),
        // (a homunculus: hd->battle_status, homunculus/data.rs; mercenaries / elementals: their battle status,
        // companion.rs)
        None => crate::mob::get(c, bl).map(|md| md.status).or_else(|| crate::homunculus::data::get(c, bl).map(|hd| hd.battle_status)).or_else(|| crate::companion::status_of(c, bl)),
    }
}

/// status.cpp status_get_status_data. Players: battle_status. The other units' status arrives with their systems
/// (phases 3 and 7); until then their HP / speed / size / mode come from the unit row over rAthena's dummy status.
pub fn status_get_status_data(c: &Ctx, bl: &Unit) -> StatusData {
    if bl.kind == defs::BL_PC {
        if let Some(sd) = data::sd_get(c, bl.id) {
            return sd.battle_status;
        }
    }
    if bl.kind == defs::BL_MOB {
        // (a monster freed during this call keeps its status for the pointers rAthena still holds: mob::get_or_freed)
        if let Some(md) = crate::mob::get_or_freed(c, bl.id) {
            return md.status;
        }
    }
    if bl.kind == defs::BL_PET {
        // pd->status (pet/data.rs)
        if let Some(pd) = crate::pet::data::get(c, bl.id) {
            return pd.status;
        }
    }
    if bl.kind == defs::BL_HOM {
        // hd->battle_status (homunculus/data.rs)
        if let Some(hd) = crate::homunculus::data::get(c, bl.id) {
            return hd.battle_status;
        }
    }
    if crate::companion::is_kind(bl.kind) {
        // md->battle_status / ed->battle_status (companion.rs)
        if let Some(s) = crate::companion::status_of(c, bl.id) {
            return s;
        }
    }
    if bl.kind == defs::BL_NPC {
        // status.cpp:9038-9046: an NPC whose class is no monster's has its own nd->status (zeroed until
        // status_calc_npc first runs: npc_data starts as zeroes), one with a monster class the dummy status
        if let Some(s) = npc_status(c, bl.id) {
            return s;
        }
    }
    let mut s = dummy_status();
    if bl.kind != defs::BL_PC && bl.max_hp > 0 {
        s.hp = bl.hp.max(0) as u32;
        s.max_hp = bl.max_hp as u32;
        s.speed = bl.speed;
        s.size = bl.size;
        s.mode = bl.mode as i32;
    }
    s
}

/// status.cpp status_get_base_status (players: base_status; monsters: md->base_status or the database status).
pub fn status_get_base_status(c: &Ctx, bl: &Unit) -> StatusData {
    if bl.kind == defs::BL_PC {
        if let Some(sd) = data::sd_get(c, bl.id) {
            return sd.base_status;
        }
    }
    if bl.kind == defs::BL_MOB {
        if let Some(md) = crate::mob::get(c, bl.id) {
            return crate::mob::status::base_status(c, &md);
        }
    }
    if bl.kind == defs::BL_HOM {
        // &hd->base_status
        if let Some(hd) = crate::homunculus::data::get(c, bl.id) {
            return hd.base_status;
        }
    }
    if crate::companion::is_kind(bl.kind) {
        // md->base_status / ed->base_status (companion.rs)
        if let Some(s) = crate::companion::base_status_of(c, bl.id) {
            return s;
        }
    }
    // (BL_NPC, status.cpp:9071: &nd->status for a class that is no monster's — status_get_status_data's answer
    // below — and nullptr for a monster class, where the callers here get the dummy status)
    status_get_status_data(c, bl)
}

/// nd->status of an NPC whose class is no monster's (`mobdb_checkid(nd->class_) == 0`, status.cpp:9041): the npc_misc
/// row's status (unit_npc.rs status_calc_npc writes it), zeroed while no command has calculated it. None for an NPC
/// with a monster class (rAthena: dummy_status) or an id that is no NPC.
fn npc_status(c: &Ctx, id: u32) -> Option<StatusData> {
    use crate::npc::npc as _;
    let nd = c.db().npc().id().find(id)?;
    if crate::data::mobdb_checkid(nd.class) {
        return None;
    }
    Some(crate::pc_misc::npcm(c, id).status)
}

/// status.cpp status_get_lv (players: base level).
pub fn status_get_lv(c: &Ctx, bl: &Unit) -> i32 {
    if bl.kind == defs::BL_PC {
        if let Some(ch) = crate::pc::character_of(c, bl.id) {
            return ch.base_level as i32;
        }
    }
    if bl.kind == defs::BL_HOM {
        // ((TBL_HOM*)bl)->homunculus.level (status.cpp:8989; the unit row's copy may lag behind a caller's row)
        if let Some(hd) = crate::homunculus::data::get(c, bl.id) {
            return hd.homunculus.level as i32;
        }
    }
    if bl.kind == defs::BL_NPC {
        // ((TBL_NPC*)bl)->level (status.cpp:8992): 0 until npcskill / setunitdata UNPC_LEVEL set it (the npc_misc row)
        return crate::pc_misc::npcm(c, bl.id).level as i32;
    }
    bl.level
}

/// `sd ? sd->status.job_level : 1`.
pub fn job_level_or_1(c: &Ctx, bl: &Unit) -> i32 {
    if bl.kind == defs::BL_PC {
        if let Some(ch) = crate::pc::character_of(c, bl.id) {
            return ch.job_level as i32;
        }
    }
    1
}

/// `sd ? sd->status.str : status_get_base_status(bl)->str`.
pub fn base_str_of(c: &Ctx, bl: &Unit) -> i32 {
    if bl.kind == defs::BL_PC {
        if let Some(ch) = crate::pc::character_of(c, bl.id) {
            return ch.str as i32;
        }
    }
    status_get_base_status(c, bl).str as i32
}

/// sd->itemid: the item a player is using (pc_useitem sets it before the item script runs; itemheal reads it). It is
/// the items system's pc_items row.
pub fn itemid(c: &Ctx, aid: u32) -> u32 {
    crate::inventory::st(c, aid).map(|s| s.itemid).unwrap_or(0)
}

/// Writes the character row (mmo_charstatus changes).
pub fn put_char(c: &Ctx, ch: &Character) {
    c.db().character().id().update(ch.clone());
}

/// map.hpp map_flag_gvg2.
pub fn map_flag_gvg2(c: &Ctx, m: u16) -> bool {
    use crate::npc::*;
    mapflag(c, m, MF_GVG) || mapflag(c, m, MF_GVG_TE) || mapflag(c, m, MF_GVG_CASTLE) || mapflag(c, m, MF_GVG_TE_CASTLE)
}

/// status.cpp status_isendure.
pub fn status_isendure(c: &Ctx, bl: u32, tick: i64, visible: bool) -> bool {
    let Some(u) = unit::get(c, bl) else { return false };
    // If bl type is set to always have endure, we don't need to check anything else
    if crate::conf::infinite_endure() & u.kind != 0 {
        return true;
    }
    // Officially the bonus "no_walk_delay" is actually just SC_ENDURE with unlimited duration.
    if u.kind == defs::BL_PC && !sc::is_disabled_on_map(c, SC_ENDURE, u.map) && data::sd_get(c, bl).map(|s| s.special_state.no_walk_delay).unwrap_or(false) {
        return true;
    }
    if u.ud.endure_tick - tick > 0 {
        return true;
    }
    let scl = sc::load(c, bl);
    if !scl.empty() {
        // Officially endure also sets endure_tick
        if scl.has(SC_ENDURE) {
            return true;
        }
        // These status changes don't send "Endure" to the client, but still prevent being stopped
        if !visible && (scl.has(SC_RUN) || scl.has(SC_WUGDASH)) {
            return true;
        }
    }
    // RENEWAL: Bosses in renewal (episode 20+) cannot be stopped by damage, but still show damage normally
    if !visible && u.kind == defs::BL_MOB && crate::mob::get(c, bl).map(|md| crate::mob::status::is_class_boss(&md)).unwrap_or(false) {
        return true;
    }
    false
}

// ---------------------------------------------------------------------------------------------- the character's life

/// pc.cpp pc_authok / pc_reg_received: the status rows of a character entering the world (regen ticks start now,
/// die_counter from its PC_DIE_COUNTER variable). Call once the `pc` and `unit` rows exist.
pub fn pc_authok(c: &Ctx, aid: u32) {
    let Some(ch) = crate::pc::character_of(c, aid) else { return };
    let class_ = crate::db::jobs::pc_jobid2mapid(ch.class);
    let mut sd = PcStatus::new(aid, ch.id, class_);
    sd.base_status.speed = DEFAULT_PC_WALK_SPEED;
    sd.battle_status.speed = DEFAULT_PC_WALK_SPEED;
    sd.die_counter = read_char_reg_num(c, aid, "PC_DIE_COUNTER") as i32;
    if let Some(old) = c.db().pc_status().account_id().find(aid) {
        c.db().pc_status().account_id().delete(old.account_id);
    }
    if let Some(old) = c.db().pc_status().char_id().find(ch.id) {
        c.db().pc_status().account_id().delete(old.account_id);
    }
    c.db().pc_status().insert(sd);
    data::put_regen_tick(c, &PcRegenTick { account_id: aid, hp: c.tick(), sp: c.tick(), ..Default::default() });
    // map_addiddb: the player is in regen_db (natural heal)
    hp::ensure_natural_heal_timer(c);
}

/// pc_readglobalreg for a permanent character variable (the script engine's `char_reg_num`).
pub fn read_char_reg_num(c: &Ctx, aid: u32, name: &str) -> i64 {
    use crate::script::vm::{CodeKey, Rt, State};
    let Some(p) = crate::pc::get(c, aid) else { return 0 };
    let rt = Rt::new(c);
    let st = State::new(CodeKey::Func(String::new()), std::rc::Rc::new(Default::default()), 0, aid, 0);
    crate::script::reg::store_get_num(&rt, &st, &crate::script::reg::Store::Char(p.char_id), &name.to_ascii_lowercase(), 0)
}

/// pc_setglobalreg for a permanent character variable.
pub fn write_char_reg_num(c: &Ctx, aid: u32, name: &str, v: i64) {
    use crate::script::vm::{CodeKey, Rt, State};
    let Some(p) = crate::pc::get(c, aid) else { return };
    let rt = Rt::new(c);
    let mut st = State::new(CodeKey::Func(String::new()), std::rc::Rc::new(Default::default()), 0, aid, 0);
    crate::script::reg::store_set_num(&rt, &mut st, &crate::script::reg::Store::Char(p.char_id), &name.to_ascii_lowercase(), 0, v);
}

/// chrif.cpp chrif_bsdata_received: the bonus scripts saved at the last logout come back.
pub fn chrif_bsdata_received(c: &Ctx, aid: u32) {
    bonus::bonus_script_load(c, aid);
}

/// chrif.cpp chrif_load_scdata: the status changes saved at the last logout (chrif_save_scdata) start again
/// (status_change_start with SCSTART_NOAVOID | SCSTART_NOTICKDEF | SCSTART_LOADED | SCSTART_NORATEDEF). The caller
/// continues with pc_scdata_received.
pub fn chrif_load_scdata(c: &Ctx, aid: u32) {
    let Some(sd) = data::sd_get(c, aid) else { return };
    // chmapif_parse_askscdata: `SELECT ... FROM sc_data WHERE account_id AND char_id` (a full scan: record slot
    // order); the rows stay in the table until the next chrif_save_scdata replaces them
    let mut rows: Vec<data::ScSaved> = c.db().sc_saved().by_char().filter(sd.char_id).collect();
    rows.sort_by_key(|r| r.pos);
    // it sends at most 50
    if rows.len() >= 50 {
        rows.truncate(50);
        log::warn!("Too many status changes for {}:{}, some of them were not loaded.", aid, sd.char_id);
    }
    if let Some(u) = unit::get(c, aid) {
        for r in rows {
            sc::status_change_start(c, None, &u, r.sc_type, 10000, r.val1, r.val2, r.val3, r.val4, r.tick, defs::SCSTART_NOAVOID | defs::SCSTART_NOTICKDEF | defs::SCSTART_LOADED | defs::SCSTART_NORATEDEF, 0);
        }
    }
}

/// pc_scdata_received's status part (after the rentals, the weight-limit notice and the cart refresh):
/// `if (sd->sc.getSCE(SC_SOULENERGY)) sd->soulball = sd->sc.getSCE(SC_SOULENERGY)->val1`.
pub fn scdata_received_soulenergy(c: &Ctx, aid: u32) {
    if sc::has_sce(c, aid, SC_SOULENERGY) {
        let v = sc::get_sce(c, aid, SC_SOULENERGY).map(|e| e.val1).unwrap_or(0);
        crate::skill::state::with(c, aid, |s| s.soulball = v as i8);
    }
}

/// clif_parse_LoadEndAck for a newly connected character: EXP / skill point values, the status window, the riding
/// icons, SC_NOCHAT for negative manner and Knowledge (Star Gladiator feel maps).
pub fn load_end_ack_connect_new(c: &Ctx, aid: u32) {
    // (clif_skillinfoblock and clif_hotkeys_send come right before this: pc.rs load_end_ack)
    for t in [SP_BASEEXP, SP_NEXTBASEEXP, SP_JOBEXP, SP_NEXTJOBEXP, SP_SKILLPOINT] {
        clif::updatestatus(c, aid, t);
    }
    clif::initialstatus(c, aid);
    let Some(u) = unit::get(c, aid) else { return };
    if u.sc.option & defs::OPTION_FALCON != 0 {
        clif::status_change(c, &u, EFST_FALCON, 1, 0, 0, 0, 0);
    } else if u.sc.option & (defs::OPTION_RIDING | defs::OPTION_DRAGON) != 0 {
        clif::status_change(c, &u, EFST_RIDING, 1, 0, 0, 0, 0);
    } else if u.sc.option & defs::OPTION_WUGRIDER != 0 {
        clif::status_change(c, &u, EFST_WUGRIDER, 1, 0, 0, 0, 0);
    } else if sc::has_sce(c, aid, SC_ALL_RIDING) {
        clif::status_change(c, &u, EFST_ALL_RIDING, 1, 0, 0, 0, 0);
    }
    if crate::pc::character_of(c, aid).map(|ch| ch.manner < 0).unwrap_or(false) {
        sc::sc_start(c, Some(&u), &u, SC_NOCHAT, 100, 0, 0);
    }
    //Auron reported that This skill only triggers when you logon on the map o.O [Skotlex]
    let lv = sc_ext::pc_checkskill(c, aid, crate::db::skills::ids::SG_KNOWLEDGE);
    if lv > 0 && sc_ext::on_feel_map(c, aid, u.map as i32) {
        let d = crate::db::skills::skill_get_time(crate::db::skills::ids::SG_KNOWLEDGE, lv as u16) as i64;
        if let Some(u) = unit::get(c, aid) {
            sc::sc_start(c, Some(&u), &u, SC_KNOWLEDGE, 100, lv, d);
        }
    }
    // the pet's greeting (clif.cpp:10939-10940, pet.rs); the homunculus' timers (clif.cpp:10942-10943)
    crate::pet::load_end_ack_connect_new(c, aid);
    crate::homunculus::load_end_ack_connect_new(c, aid);
    crate::pc_misc::loadendack_night_connect(c, aid);
}

/// clif_parse_LoadEndAck on a warp: the client "loses" the stats.
pub fn load_end_ack_refresh(c: &Ctx, aid: u32) {
    for t in [SP_STR, SP_AGI, SP_VIT, SP_INT, SP_DEX, SP_LUK, SP_POW, SP_STA, SP_WIS, SP_SPL, SP_CON, SP_CRT] {
        clif::updatestatus(c, aid, t);
    }
}

/// clif_parse_LoadEndAck on a map change: the flee penalty of GvG / battleground maps, status changes that end on
/// map change, and the map-dependent recalculation.
pub fn load_end_ack_changemap(c: &Ctx, aid: u32, pmap: u16) {
    let Some(u) = unit::get(c, aid) else { return };
    if crate::conf::bg_flee_penalty() != 100 || crate::conf::gvg_flee_penalty() != 100 {
        let gvg_or_bg = |m: u16| crate::npc::mapflag_gvg(c, m) || crate::npc::mapflag(c, m, crate::npc::MF_BATTLEGROUND);
        if gvg_or_bg(pmap) || gvg_or_bg(u.map) {
            calc_bl(c, aid, 1u64 << SCB_FLEE, SCO_NONE); // Refresh flee penalty
        }
    }
    crate::pc_misc::loadendack_night_changemap(c, aid); // Display / clear night
    crate::clif_bg::load_end_ack_changemap(c, aid); // Battleground Mode (clif.cpp:11071-11076)
    // (PC_PERM_VIEW_HPMETER: hpmeter_visible is not counted; clif_update_hp looks for such viewers in the area)
    sc::status_change_clear_on_change_map(c, &u);
    crate::map_cell::iwall_get(c, &u); // Updates Walls Info on this Map to Client
    // Some conditions are map-dependent so we must recalculate (SCO_FIRST for an autotrader, autotrade.rs)
    calc_pc(c, aid, if crate::autotrade::autotrade(c, aid) != 0 { SCO_FIRST } else { SCO_NONE });
}

/// pc_setstand's status part: Tension Relax ends (before the icon and the standing packet).
pub fn on_stand_before(c: &Ctx, aid: u32) {
    sc::status_change_end(c, aid, SC_TENSIONRELAX, 0);
}

/// pc_setstand's status part: the sitting regen ticks reset.
pub fn on_stand_after(c: &Ctx, aid: u32) {
    let mut t = data::regen_tick(c, aid);
    t.ss_hp = 0;
    t.ss_sp = 0;
    data::put_regen_tick(c, &t);
}

/// pc.cpp pc_setrestartvalue (pc.rs calls it with rAthena's type).
pub fn restart_value(c: &Ctx, aid: u32, type_: i32) {
    hp::pc_setrestartvalue(c, aid, type_);
}

/// pc_makesavestatus's HP part: the battle values (or the respawn values when dead) go to the character.
pub fn makesavestatus_hp(c: &Ctx, aid: u32, ch: &mut Character, dead: bool) {
    let Some(sd) = data::sd_get(c, aid) else { return };
    if dead {
        // pc_setrestartvalue(sd, 0)
        let b = &sd.base_status;
        let st = &sd.battle_status;
        ch.hp = b.hp;
        ch.sp = if st.sp < b.sp { b.sp } else { st.sp };
        ch.ap = if st.ap < b.ap { b.ap } else { st.ap };
    } else {
        ch.hp = sd.battle_status.hp;
        ch.sp = sd.battle_status.sp;
        ch.ap = sd.battle_status.ap;
    }
}

/// map_quit's status part, before the character is saved: status changes that are not saved end, and the rest are
/// kept for the next login (chrif_save_scdata); bonus scripts are saved (chrif_bsdata_save).
pub fn on_quit_before_save(c: &Ctx, aid: u32) {
    let Some(sd) = data::sd_get(c, aid) else { return };
    let scl = sc::load(c, aid);
    if !scl.empty() {
        for s in crate::db::misc::status::status_db().iter() {
            let t = s.type_;
            let Some(e) = sc::get_sce(c, aid, t) else { continue };
            // No need to save infinite status
            if s.has_flag(SCF_NOSAVEINFINITE) && (e.val4 > 0 || e.timer == 0) {
                sc::status_change_end(c, aid, t, 0);
                continue;
            }
            // Status that are not saved
            if s.has_flag(SCF_NOSAVE) {
                sc::status_change_end(c, aid, t, 0);
                continue;
            }
            // Removes status by config
            if (crate::conf::debuff_on_logout() & 1 != 0 && s.has_flag(SCF_DEBUFF)) || (crate::conf::debuff_on_logout() & 2 != 0 && !s.has_flag(SCF_DEBUFF)) {
                sc::status_change_end(c, aid, t, 0);
                continue;
            }
        }
    }
    // (sd->state.permanent_speed is cleared: the row goes)
    let _ = sd;
}

/// chrif_save (CSAVE_QUIT) for the status system: chrif_save_scdata and chrif_bsdata_save.
pub fn on_quit_save(c: &Ctx, aid: u32) {
    let Some(sd) = data::sd_get(c, aid) else { return };
    let tick = c.tick();
    // chrif_save_scdata: the remaining time of every status change (INFINITE_TICK for those without a timer)
    // chmapif_parse_save_scdata: "Whatever comes from the mapserver, now is the time to drop previous entries"
    sc_data_delete_char(c.db(), sd.char_id);
    // for (const auto& [type, sce] : *sc): the unordered_map's order (the char server stores and returns the rows in it)
    for t in sc::load_map_order(c, aid).iter().map(|e| e.sc_type) {
        let Some(e) = sc::get_sce(c, aid, t) else { continue };
        let left = if e.timer != 0 {
            match crate::timer::due(c, e.timer) {
                Some(due) if due - tick > 0 => due - tick,
                Some(_) => 0, // Negative tick does not necessarily mean that sc has expired
                None => continue,
            }
        } else {
            sc::INFINITE_TICK
        };
        // the multi-row INSERT, in the packet's order
        let pos = sc_data_take_slot(c.db());
        c.db().sc_saved().insert(data::ScSaved { id: 0, pos, char_id: sd.char_id, sc_type: t, tick: left, val1: e.val1, val2: e.val2, val3: e.val3, val4: e.val4 });
    }
    bonus::bonus_script_save_on_quit(c, aid);
}

/// The `sc_data_slots` row (created from the table's highest slot when missing).
fn sc_data_slots(db: &spacetimedb::Local) -> data::ScDataSlots {
    use data::sc_data_slots;
    match db.sc_data_slots().id().find(0) {
        Some(s) => s,
        None => {
            let next = db.sc_saved().iter().map(|r| r.pos + 1).max().unwrap_or(0);
            db.sc_data_slots().insert(data::ScDataSlots { id: 0, next, free: Vec::new() })
        }
    }
}

/// A record slot for a new `sc_data` row: the last freed one first, else a new one at the end.
fn sc_data_take_slot(db: &spacetimedb::Local) -> u64 {
    use data::sc_data_slots;
    let mut s = sc_data_slots(db);
    let pos = match s.free.pop() {
        Some(p) => p,
        None => {
            s.next += 1;
            s.next - 1
        }
    };
    db.sc_data_slots().id().update(s);
    pos
}

/// `DELETE FROM sc_data WHERE account_id = ? AND char_id = ?` (chmapif_parse_save_scdata, char_delete): the rows go
/// in record order (a full scan), each slot onto the free list (an emptied table keeps its free slots: checked on the
/// oracle's MariaDB 10.11, SHOW TABLE STATUS Rows 0 / Data_free > 0, and new rows take the last freed slot first).
pub fn sc_data_delete_char(db: &spacetimedb::Local, char_id: u32) {
    use data::sc_data_slots;
    let mut rows: Vec<data::ScSaved> = db.sc_saved().by_char().filter(char_id).collect();
    if rows.is_empty() {
        return;
    }
    rows.sort_by_key(|r| r.pos);
    let mut s = sc_data_slots(db);
    for r in rows {
        db.sc_saved().id().delete(r.id);
        s.free.push(r.pos);
    }
    db.sc_data_slots().id().update(s);
}

/// unit_free for a player: status_change_clear(bl, 1), then every status row goes.
pub fn on_unit_free(c: &Ctx, aid: u32) {
    for which in 1..=3 {
        bonus::pc_delautobonus(c, aid, which, false);
    }
    sc::status_change_clear(c, aid, 1);
    sc::free_unit(c, aid);
    if let Some(sd) = data::sd_get(c, aid) {
        // (bonus_script entries left online were saved by on_quit_save; anything else goes)
        let ids: Vec<u64> = c.db().bonus_script().by_char().filter(sd.char_id).filter(|b| b.online).map(|b| b.id).collect();
        for id in ids {
            c.db().bonus_script().id().delete(id);
        }
    }
    c.db().pc_status().account_id().delete(aid);
    c.db().pc_regen_tick().account_id().delete(aid);
}

use data::{bonus_script, pc_regen_tick, pc_status, sc_saved};
