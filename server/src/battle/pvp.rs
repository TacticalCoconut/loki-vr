//! PvP / GvG map rules: pc.cpp pc_calc_pvprank / pc_calc_pvprank_sub / pc_calc_pvprank_timer, the PvP part of
//! clif_parse_LoadEndAck and unit_remove_map, map.cpp map_setmapflag_sub's PvP / GvG side effects
//! (map_mapflag_pvp_start_sub / stop_sub, clif_map_property_mapall, unit_stopattack), clif_pvpset.

use super::*;
use crate::clif::{self, Target, MAPPROPERTY_AGITZONE, MAPPROPERTY_FREEPVPZONE, MAPPROPERTY_NOTHING};
use crate::map::{self, BL_CHAR};
use crate::npc::*;
use crate::status::defs::{OPTION_CLOAK, OPTION_HIDE, OPTION_INVISIBLE};
use crate::timer::{self, Func};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// pc.hpp PVP_CALCRANK_INTERVAL.
pub const PVP_CALCRANK_INTERVAL: Tick = 1000;

fn invisible(u: &Unit) -> bool {
    u.sc.option & OPTION_INVISIBLE != 0
}

/// map_data::users_pvp: the players on the map that are not GM-invisible (rAthena counts them up at LoadEndAck and
/// down in unit_remove_map / @hide; here they are counted when asked).
pub fn users_pvp(c: &Ctx, m: u16) -> u32 {
    map::players_on_map(c, m).iter().filter(|u| !invisible(u)).count() as u32
}

/// clif.cpp clif_pvpset: ZC_NOTIFY_RANKING. type 0: to the area, 1: to the map, 2: to the player only (with the
/// raw values).
pub fn clif_pvpset(c: &Ctx, sd: &Unit, pvprank: u32, pvpnum: u32, type_: i32) {
    let mut w = Vec::with_capacity(14);
    w.extend_from_slice(&0x019au16.to_le_bytes());
    w.extend_from_slice(&sd.id.to_le_bytes());
    if type_ == 2 {
        w.extend_from_slice(&pvprank.to_le_bytes());
        w.extend_from_slice(&pvpnum.to_le_bytes());
        clif::send(c, &w, sd, Target::SelfOnly);
        return;
    }
    if sd.sc.option & (OPTION_HIDE | OPTION_CLOAK) != 0 {
        w.extend_from_slice(&u32::MAX.to_le_bytes()); // On client displays as --
    } else {
        w.extend_from_slice(&pvprank.to_le_bytes());
    }
    w.extend_from_slice(&pvpnum.to_le_bytes());
    // Causes crashes when a 'mob' with pvp info dies.
    if invisible(sd) || crate::atcmd_disguise::disguised(c, sd) {
        clif::send(c, &w, sd, Target::SelfOnly);
    } else if type_ == 0 {
        clif::send(c, &w, sd, Target::Area);
    } else {
        clif::send(c, &w, sd, Target::AllSamemap);
    }
}

/// pc.cpp pc_calc_pvprank: the player's rank among the players of the map (pc_calc_pvprank_sub over
/// map_foreachinmap), sent when it or the number of players changed.
pub fn pc_calc_pvprank(c: &Ctx, aid: u32) -> u32 {
    let Some(sd) = unit::get(c, aid) else { return 0 };
    let mut b = state::get(c, aid);
    let old = b.pvp_rank;
    b.pvp_rank = 1;
    for other in map::players_on_map(c, sd.map) {
        // cannot register pvp rank for hidden GMs
        if invisible(&other) || invisible(&sd) {
            continue;
        }
        if state::get(c, other.id).pvp_point > b.pvp_point {
            b.pvp_rank += 1;
        }
    }
    let users = users_pvp(c, sd.map);
    if old != b.pvp_rank || b.pvp_lastusers != users {
        b.pvp_lastusers = users;
        state::put(c, &b);
        clif_pvpset(c, &sd, b.pvp_rank, users, 0);
    } else {
        state::put(c, &b);
    }
    b.pvp_rank
}

/// pc.cpp pc_calc_pvprank_timer (Func::PcCalcPvprank).
pub fn pc_calc_pvprank_timer(c: &Ctx, _tid: u64, _tick: Tick, aid: u32, data: i64) {
    let Some(sd) = unit::get(c, aid) else { return };
    if crate::pc::get(c, aid).is_none() {
        return;
    }
    state::with(c, aid, |b| b.pvp_timer = 0);
    if invisible(&sd) {
        return; // do not calculate the pvp rank for a hidden GM
    }
    if pc_calc_pvprank(c, aid) > 0 {
        let tid = timer::add(c, c.tick() + PVP_CALCRANK_INTERVAL, Func::PcCalcPvprank, aid, data);
        state::with(c, aid, |b| b.pvp_timer = tid);
    }
}

/// clif.cpp clif_parse_LoadEndAck, the PvP registration before the map property: on a PvP map (outside pk_mode) a
/// visible player starts the ranking timer with 5 points.
pub fn load_end_ack(c: &Ctx, aid: u32) {
    let Some(sd) = unit::get(c, aid) else { return };
    if !invisible(&sd) && mapflag(c, sd.map, MF_PVP) && crate::conf::pk_mode() == 0 {
        // remove pvp stuff for pk_mode [Valaris]
        let timer = if !mapflag(c, sd.map, super::target::MF_PVP_NOCALCRANK) { timer::add(c, c.tick() + 200, Func::PcCalcPvprank, aid, 0) } else { 0 };
        state::with(c, aid, |b| {
            if timer != 0 {
                b.pvp_timer = timer;
            }
            b.pvp_rank = 0;
            b.pvp_lastusers = 0;
            b.pvp_point = 5;
            b.pvp_won = 0;
            b.pvp_lost = 0;
        });
    }
}

/// unit.cpp unit_remove_map_'s PvP part for players: the ranking timer stops.
pub fn remove_map(c: &Ctx, aid: u32) {
    let b = state::get(c, aid);
    if b.pvp_timer != 0 {
        timer::delete(c, b.pvp_timer);
        state::with(c, aid, |b| {
            b.pvp_timer = 0;
            b.pvp_rank = 0;
        });
    }
}

/// clif.cpp clif_map_property_mapall: the map property to every player on the map (sent for a block_list with id 0,
/// so the duel bits are never set). `flag_of` answers the map flags' values as they are when rAthena sends it.
fn map_property_mapall(c: &Ctx, m: u16, property: u16, flag_of: &dyn Fn(i16) -> i32) {
    let w = map_property_packet(c, property, flag_of, false);
    for u in map::players_on_map(c, m) {
        clif::send(c, &w, &u, Target::SelfOnly);
    }
}

/// clif.cpp clif_map_property's 0x099b with the map flags `flag_of` answers; `duel`: sent for a player in a duel
/// (sd->duel_group > 0, duel.rs). Terms that shift getMapFlag's value itself (not a `||`) shift the value:
/// MF_BATTLEGROUND 2 sets bit 8, not bit 7 (bg agent).
fn map_property_packet(c: &Ctx, property: u16, flag_of: &dyn Fn(i16) -> i32, duel: bool) -> Vec<u8> {
    let f = |flag| (flag_of(flag) != 0) as u32;
    let v = |flag| flag_of(flag) as u32;
    let duel = duel as u32;
    // mapdata_flag_gvg2 / mapdata_flag_vs on those flags
    let gvg2 = f(MF_GVG) | f(MF_GVG_TE) | f(MF_GVG_CASTLE) | f(MF_GVG_TE_CASTLE);
    let agit = (crate::guild::agit_flag(c) || crate::guild::agit2_flag(c)) as u32;
    let agit3 = crate::guild::agit3_flag(c) as u32;
    let vs = f(MF_PVP) | f(MF_GVG_DUNGEON) | f(MF_GVG) | (agit & f(MF_GVG_CASTLE)) | f(MF_GVG_TE) | (agit3 & f(MF_GVG_TE_CASTLE)) | f(MF_BATTLEGROUND);
    let flags = (f(MF_PVP) | duel)
        | ((f(MF_BATTLEGROUND) | gvg2) << 1)
        | ((f(MF_BATTLEGROUND) | gvg2) << 2)
        | ((f(MF_FORCEMINEFFECT) | gvg2) << 3)
        | ((f(MF_NOLOCKON) | vs | duel) << 4)
        | (v(MF_PVP) << 5)
        | (v(MF_PARTYLOCK) << 6)
        | (v(MF_BATTLEGROUND) << 7)
        | (v(MF_NOCOSTUME) << 8)
        | ((1 - f(MF_NOUSECART)) << 9)
        | ((1 - f(MF_NOSUNMOONSTARMIRACLE)) << 10);
    let mut w = Vec::with_capacity(8);
    w.extend_from_slice(&0x099bu16.to_le_bytes());
    w.extend_from_slice(&property.to_le_bytes());
    w.extend_from_slice(&flags.to_le_bytes());
    w
}

/// map.cpp map_mapflag_pvp_start_sub. Its clif_map_property(sd, MAPPROPERTY_FREEPVPZONE, SELF) runs before
/// map_setmapflag_sub removes the conflicting GvG / battleground flags, so it sends the flags `flag_of` answers.
fn pvp_start_sub(c: &Ctx, sd: &Unit, flag_of: &dyn Fn(i16) -> i32) {
    if state::get(c, sd.id).pvp_timer == 0 {
        let tid = timer::add(c, c.tick() + 200, Func::PcCalcPvprank, sd.id, 0);
        state::with(c, sd.id, |b| {
            b.pvp_timer = tid;
            b.pvp_rank = 0;
            b.pvp_lastusers = 0;
            b.pvp_point = 5;
            b.pvp_won = 0;
            b.pvp_lost = 0;
        });
    }
    clif::send(c, &map_property_packet(c, MAPPROPERTY_FREEPVPZONE, flag_of, crate::duel::duel_group(c, sd.id) > 0), sd, Target::SelfOnly);
}

/// map.cpp map_mapflag_pvp_stop_sub.
fn pvp_stop_sub(c: &Ctx, sd: &Unit) {
    clif_pvpset(c, sd, 0, 0, 2);
    let b = state::get(c, sd.id);
    if b.pvp_timer != 0 {
        timer::delete(c, b.pvp_timer);
        state::with(c, sd.id, |b| b.pvp_timer = 0);
    }
}

/// map_foreachinmap(unit_stopattack, m, BL_CHAR, 0): every unit of the map stops its auto-attack.
fn stopattack_map(c: &Ctx, m: u16) {
    let md = crate::data::maps().get(m);
    let (xs, ys) = md.map(|d| (d.xs, d.ys)).unwrap_or((0, 0));
    for u in map::units_in_area(c, m, 0, 0, xs - 1, ys - 1, BL_CHAR) {
        super::unit_attack::unit_stopattack(c, &u, 0);
    }
}

/// map.cpp map_setmapflag_sub with its run-time effects for PvP and GvG (the other flags go straight to npc.rs
/// setmapflag, which keeps the rules of which flags exclude which). The map property packet is sent with the flags
/// as rAthena has them at that moment: the new flag set, the conflicting ones not removed yet.
pub fn map_setmapflag(c: &Ctx, m: u16, flag: i16, status: bool, arg: i32) {
    match flag {
        MF_PVP => {
            let before: Vec<(i16, i32)> = [MF_GVG, MF_GVG_TE, MF_GVG_DUNGEON, MF_GVG_CASTLE, MF_GVG_TE_CASTLE, MF_BATTLEGROUND].into_iter().map(|f| (f, mapflag_value(c, m, f))).filter(|&(_, v)| v != 0).collect();
            let conflicts: Vec<i16> = before.iter().map(|&(f, _)| f).collect();
            setmapflag(c, m, MF_PVP, status, arg); // (also removes the conflicting flags)
            let flag_of = |f: i16| if f == MF_PVP { status as i32 } else if let Some(&(_, v)) = before.iter().find(|&&(g, _)| g == f) { v } else { mapflag_value(c, m, f) };
            if !status {
                map_property_mapall(c, m, MAPPROPERTY_NOTHING, &flag_of);
                for u in map::players_on_map(c, m) {
                    pvp_stop_sub(c, &u);
                }
                stopattack_map(c, m);
            } else if crate::conf::pk_mode() == 0 {
                map_property_mapall(c, m, MAPPROPERTY_FREEPVPZONE, &flag_of);
                for u in map::players_on_map(c, m) {
                    pvp_start_sub(c, &u, &flag_of);
                }
            }
            for f in conflicts.into_iter().filter(|_| status) {
                let name = crate::data::maps().get(m).map(|d| d.name.clone()).unwrap_or_default();
                log::warn!("map_setmapflag: Unable to set flag {f} and PvP flags for the same map! Removing it from {name}.");
            }
        }
        MF_GVG | MF_GVG_TE => {
            let pvp_before = mapflag_value(c, m, MF_PVP);
            let bg_before = mapflag_value(c, m, MF_BATTLEGROUND);
            setmapflag(c, m, flag, status, arg);
            // flags as rAthena sends them: the GvG flag changed, PvP / battleground not removed yet
            let flag_of = |f: i16| if f == MF_PVP { pvp_before } else if f == MF_BATTLEGROUND { bg_before } else { mapflag_value(c, m, f) };
            if !status {
                map_property_mapall(c, m, MAPPROPERTY_NOTHING, &flag_of);
                stopattack_map(c, m);
            } else {
                map_property_mapall(c, m, MAPPROPERTY_AGITZONE, &flag_of);
            }
        }
        _ => setmapflag(c, m, flag, status, arg),
    }
}

