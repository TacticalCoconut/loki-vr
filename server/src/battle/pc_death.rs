//! pc.cpp pc_dead (the player part of dying), pc_respawn_timer and the Star Gladiator's hated monsters
//! (sd->hate_mob) battle reads. The killer / killed ids (SP_KILLERRID, SP_KILLEDRID, SP_KILLEDGID) live in the mob
//! port's `pc_kill` row; pc_dead sets them through pc_setparam.
//!
//! status/hp.rs status_damage calls pc_dead; its return value keeps rAthena's meaning (0: death cancelled, &8: the
//! map's respawn timer is set).

use super::*;
use crate::db::jobs::*;
use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::npc::*;
use crate::status::consts::*;
use crate::status::defs::*;
use crate::status::sc;
use crate::timer::{self, Func};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// itemdb.hpp ITEMID_NEW_INSURANCE (Life Insurance).
const ITEMID_NEW_INSURANCE: u32 = 6413;
/// pc_dead's pc_delitem `reason` 1 (clif_delitem's ZC_DELETE_ITEM_FROM_BODY delete type).
const DELITEM_CONSUMED: i16 = 1;

/// sd->hate_mob[MAX_PC_FEELHATE] (crate::pc_taekwon, the one owner of the feel / hate fields).
pub fn hate_mobs(c: &Ctx, aid: u32) -> [i32; super::calc::MAX_PC_FEELHATE] {
    crate::pc_taekwon::hate_mobs(c, aid)
}

/// common/utils.cpp get_percentage_exp.
fn get_percentage_exp(a: u64, b: u64) -> u32 {
    if b == 0 {
        log::error!("get_percentage_exp: divison by zero! (a={a},b={b})");
        return !0u32;
    }
    let result = 100.0 * (a as f64 / b as f64);
    if result > u32::MAX as f64 {
        return u32::MAX;
    }
    result.floor() as u32
}

/// pc.cpp pc_dead: a player dies. 0: death cancelled (the Super Novice's angel); 1 | 8 when the map respawns the
/// player itself (PvP ranking below zero, GvG maps), otherwise 1.
pub fn pc_dead(c: &Ctx, aid: u32, src: Option<&Unit>) -> i32 {
    let tick = c.tick();
    let Some(u) = unit::get(c, aid) else { return 1 };
    let Some(sd) = crate::status::data::sd_get(c, aid) else { return 1 };
    let Some(ch) = crate::pc::character_of(c, aid) else { return 1 };
    let m = u.map;
    // Activate Steel body if a super novice dies at 99+% exp [celest]
    // Super Novices have no kill or die functions attached when saved by their angel
    if !state::get(c, aid).snovice_dead_flag && sd.class_ & MAPID_SECONDMASK == MAPID_SUPER_NOVICE {
        let exp = pc_nextbaseexp(ch.class, ch.base_level as u32);
        if exp != 0 && get_percentage_exp(ch.base_exp, exp) >= 99 {
            state::with(c, aid, |b| b.snovice_dead_flag = true);
            crate::pc::restart_value(c, aid, 1);
            crate::status::hp::status_percent_heal(c, aid, 100, 100, 0);
            if let Some(u) = unit::get(c, aid) {
                crate::status::clif::resurrection(c, &u);
            }
            crate::pc::setinvincibletimer(c, aid);
            if let Some(u) = unit::get(c, aid) {
                sc::sc_start(c, Some(&u), &u, SC_STEELBODY, 100, 5, skill_get_time(MO_STEELBODY, 5) as i64);
            }
            if mapflag_gvg2(c, m) {
                pc_respawn_timer(c, 0, tick, aid);
            }
            return 0;
        }
    }
    // Devotion, Stellar Marks, United Soul, Servant Sign and Shadow Form links (pc.cpp:9795-9837; the skill fields
    // are skill/state.rs's): each linked player's status ends and the slot is cleared
    {
        use crate::skill::state as st;
        let s = st::get(c, aid);
        let links: [(&Vec<u32>, usize, i32); 4] = [
            (&s.devotion, st::MAX_DEVOTION, SC_DEVOTION),
            (&s.stellar_mark, st::MAX_STELLAR_MARKS, SC_FLASHKICK),
            (&s.united_soul, st::MAX_UNITED_SOULS, SC_SOULUNITY),
            (&s.servant_sign, st::MAX_SERVANT_SIGN, SC_SERVANT_SIGN),
        ];
        for (list, max, t) in links {
            for k in 0..max {
                let id = list.get(k).copied().unwrap_or(0);
                if id != 0 {
                    // map_id2sd: a player only
                    if crate::pc::get(c, id).is_some() {
                        sc::status_change_end(c, id, t, 0);
                    }
                }
            }
        }
        st::with(c, aid, |s| {
            for v in [&mut s.devotion, &mut s.stellar_mark, &mut s.united_soul, &mut s.servant_sign] {
                for x in v.iter_mut() {
                    *x = 0;
                }
            }
        });
        let shadowform_id = st::get(c, aid).shadowform_id;
        if shadowform_id != 0 {
            //if we were target of shadowform
            sc::status_change_end(c, shadowform_id, SC__SHADOWFORM, 0);
            st::with(c, aid, |s| s.shadowform_id = 0); //should be remove on status end anyway
        }
    }
    // the pet's intimacy (pc.cpp:9839-9847, pet.rs); homunculus_auto_vapor (pc.cpp:9849-9850, homunculus.rs);
    // `if( sd->md ) mercenary_delete(sd->md, 3)` and `if( sd->ed ) elemental_delete(sd->ed)` (pc.cpp:9852-9856)
    crate::pet::pc_dead(c, aid);
    crate::homunculus::pc_dead(c, aid);
    crate::mercenary::pc_dead(c, aid);
    crate::elemental::master_drops(c, aid);
    // Leave duel if you die [LuzZza] (duel_autoleave_when_die, duel.rs)
    crate::duel::pc_dead(c, aid);
    // skill_keep_using's timer
    let keep_tid = crate::skill::state::get(c, aid).keep_using_tid;
    if keep_tid != 0 {
        crate::timer::delete(c, keep_tid);
        crate::skill::state::with(c, aid, |s| s.keep_using_tid = 0);
    }
    crate::script::npc_run::pc_close_npc(c, aid, 2); // close npc if we were using one
    // e.g. not killed thru pc_damage
    if let Some(u) = unit::get(c, aid) {
        if u.vd.dead_sit == 2 {
            crate::status::clif::status_change(c, &u, EFST_SIT, 0, 0, 0, 0, 0);
        }
    }
    // pc_setdead
    if let Some(mut p) = crate::pc::get(c, aid) {
        p.dead_sit = 1;
        crate::pc::put(c, &p);
    }
    if let Some(mut u) = unit::get(c, aid) {
        u.vd.dead_sit = 1;
        u.dead = true;
        unit::save(c, &u);
        crate::party::clif_party_dead(c, &u);
    }
    let die_counter = crate::status::data::sd_get(c, aid).map(|s| s.die_counter).unwrap_or(0);
    crate::status::param::setparam(c, aid, SP_PCDIECOUNTER as i64, die_counter as i64 + 1);
    crate::status::param::setparam(c, aid, SP_KILLERRID as i64, src.map(|s| s.id as i64).unwrap_or(0));
    if conf::loose_ap_on_death() {
        crate::status::hp::status_percent_damage(c, None, aid, 0, 0, 100, false);
    }
    //Reset menu skills/item skills
    if crate::skill::state::get(c, aid).skillitem != 0 {
        crate::skill::state::with(c, aid, |s| {
            s.skillitem = 0;
            s.skillitemlv = 0;
        });
    }
    if crate::itemui::menu(c, aid).0 != 0 {
        let (_, _, val2) = crate::itemui::menu(c, aid);
        crate::itemui::set_menu(c, aid, 0, 0, val2);
    }
    // Reset ticks.
    crate::status::data::with_sd(c, aid, |sd| {
        for it in sd.lists.hp_loss.iter_mut() {
            it.tick = 0;
        }
        for it in sd.lists.sp_loss.iter_mut() {
            it.tick = 0;
        }
        for it in sd.lists.hp_regen.iter_mut() {
            it.tick = 0;
        }
        for it in sd.lists.sp_regen.iter_mut() {
            it.tick = 0;
        }
    });
    // pc.cpp pc_dead: the spirit spheres, soul energy, servant weapons, abyss spheres and spirit charms go
    {
        use crate::skill::state as st;
        let s = st::get(c, aid);
        if s.spiritball != 0 {
            st::pc_delspiritball(c, aid, s.spiritball as i32, 0);
        }
        if s.soulball != 0 {
            st::pc_delsoulball(c, aid, s.soulball as i32, false);
        }
        if s.servantball != 0 {
            st::pc_delservantball(c, aid, s.servantball as i32);
        }
        if s.abyssball != 0 {
            st::pc_delabyssball(c, aid, s.abyssball as i32);
        }
        if s.spiritcharm_type != st::CHARM_TYPE_NONE && s.spiritcharm > 0 {
            st::pc_delspiritcharm(c, aid, s.spiritcharm as i32, s.spiritcharm_type);
        }
    }
    let mut src = src.cloned();
    if let Some(s) = src.clone() {
        match s.kind {
            BL_MOB => {
                if super::mobif::target_id(c, &s) == aid {
                    super::mobif::unlocktarget(c, &s, tick);
                }
                // mobs_level_up: no
                src = Some(super::target::battle_get_master(c, &s)); // Maybe Player Summon
            }
            //Pass on to master... (pc.cpp:9939-9943: BL_PET, BL_HOM, BL_MER)
            BL_PET | crate::map::BL_HOM | crate::map::BL_MER => src = Some(super::target::battle_get_master(c, &s)),
            _ => {}
        }
    }
    if let Some(ssd) = src.as_ref().filter(|s| s.kind == BL_PC) {
        crate::status::param::setparam(c, ssd.id, SP_KILLEDRID as i64, aid as i64);
        crate::script::npc_run::npc_script_event(c, ssd.id, crate::script::npc_run::NPCE_KILLPC);
        // pk_mode&2 manner loss: pk_mode is 0
    }
    // bone_drop: 0 (no skull)
    // Remove bonus_script when dead
    crate::status::bonus::pc_bonus_script_clear(c, aid, BSF_REM_ON_DEAD);
    // changed penalty options, added death by player if pk_mode [Valaris]
    let sd = crate::status::data::sd_get(c, aid).unwrap_or(sd);
    let ch = crate::pc::character_of(c, aid).unwrap_or(ch);
    if conf::death_penalty_type() != 0
        && sd.class_ & MAPID_SECONDMASK != MAPID_NOVICE // only novices will receive no penalty
        && !sc::cant(c, aid, SCS_NODEATHPENALTY)
        && !mapflag(c, m, MF_NOEXPPENALTY)
        && !mapflag_gvg2(c, m)
    {
        // (VIP: off)
        let mut base_penalty: u64 = conf::death_penalty_base();
        let mut job_penalty: u64 = conf::death_penalty_job();
        let mut zeny_penalty: u32 = conf::zeny_penalty();
        if (conf::death_penalty_maxlv() & 1 != 0 || !pc_is_maxbaselv(ch.class, ch.base_level as u32)) && base_penalty > 0 {
            match conf::death_penalty_type() {
                1 => base_penalty = (pc_nextbaseexp(ch.class, ch.base_level as u32) as f64 * (base_penalty as f64 / 10000.0)) as u64,
                2 => base_penalty = (ch.base_exp as f64 * (base_penalty as f64 / 10000.0)) as u64,
                _ => {}
            }
            if base_penalty != 0 {
                // (pk_mode doubles it against players: pk_mode is 0)
                base_penalty = ch.base_exp.min(base_penalty);
            }
        } else {
            base_penalty = 0;
        }
        if (conf::death_penalty_maxlv() & 2 != 0 || !pc_is_maxjoblv(ch.class, ch.job_level as u32)) && job_penalty > 0 {
            match conf::death_penalty_type() {
                // (uint32) casts as rAthena
                1 => job_penalty = (pc_nextjobexp(ch.class, ch.job_level as u32) as f64 * (job_penalty as f64 / 10000.0)) as u32 as u64,
                2 => job_penalty = (ch.job_exp as f64 * (job_penalty as f64 / 10000.0)) as u32 as u64,
                _ => {}
            }
            if job_penalty != 0 {
                job_penalty = ch.job_exp.min(job_penalty);
            }
        } else {
            job_penalty = 0;
        }
        if base_penalty != 0 || job_penalty != 0 {
            let insurance_idx = crate::inventory::pc_search_inventory(c, aid, ITEMID_NEW_INSURANCE);
            if insurance_idx < 0 || crate::inventory::pc_delitem(c, aid, insurance_idx as i32, 1, 0, DELITEM_CONSUMED, crate::map_log::LOG_TYPE_CONSUME) != 0 {
                crate::status::progress::pc_lostexp(c, aid, base_penalty, job_penalty);
            }
        }
        if zeny_penalty > 0 && !mapflag(c, m, MF_NOZENYPENALTY) {
            let zeny = crate::pc::character_of(c, aid).map(|ch| ch.zeny).unwrap_or(0);
            zeny_penalty = (zeny as f64 * (zeny_penalty as f64 / 10000.0)) as u32;
            if zeny_penalty != 0 {
                crate::inventory::pc_payzeny(c, aid, zeny_penalty as i64, crate::map_log::LOG_TYPE_PICKDROP_PLAYER, 0);
            }
        }
    }
    if mapflag(c, m, MF_PVP_NIGHTMAREDROP) {
        // the map's drop_list is not kept by the mapflag parser yet (npc.rs stores the flag only)
        log::warn!("pc_dead: pvp_nightmaredrop on map {m}: the drop list is not stored (mapflag parser)");
    }
    // pvp: disable certain pvp functions on pk_mode [Valaris]
    if crate::conf::pk_mode() == 0 && mapflag(c, m, MF_PVP) && !mapflag(c, m, super::target::MF_PVP_NOCALCRANK) {
        let point = state::with(c, aid, |b| {
            b.pvp_point -= 5;
            b.pvp_lost += 1;
            b.pvp_point
        });
        if let Some(ssd) = src.as_ref().filter(|s| s.kind == BL_PC) {
            state::with(c, ssd.id, |b| {
                b.pvp_point += 1;
                b.pvp_won += 1;
            });
        }
        if point < 0 {
            let tid = timer::add(c, tick + 1000, Func::PcRespawn, aid, 0);
            state::with(c, aid, |b| b.respawn_tid = tid);
            return 1 | 8;
        }
    }
    // GvG
    if mapflag_gvg2(c, m) {
        let tid = timer::add(c, tick + 1000, Func::PcRespawn, aid, 0);
        state::with(c, aid, |b| b.respawn_tid = tid);
        return 1 | 8;
    }
    // else if( sd->bg_id ): Respawn by BG when the team has a cemetery (pc.cpp:10132-10141)
    else if crate::battleground::pc_dead_respawn(c, aid) {
        let tid = timer::add(c, tick + 1000, Func::PcRespawn, aid, 0);
        state::with(c, aid, |b| b.respawn_tid = tid);
        return 1 | 8;
    }
    // Reset "can log out" tick.
    if crate::conf::prevent_logout() != 0 {
        if let Some(mut p) = crate::pc::get(c, aid) {
            p.canlog_tick = c.tick() - crate::conf::prevent_logout();
            crate::pc::put(c, &p);
        }
    }
    1
}

/// pc.cpp pc_respawn_timer (Func::PcRespawn; also called directly with tid 0).
pub fn pc_respawn_timer(c: &Ctx, _tid: u64, _tick: Tick, aid: u32) {
    if crate::pc::get(c, aid).is_none() {
        return;
    }
    state::with(c, aid, |b| {
        b.pvp_point = 0;
        b.respawn_tid = 0;
    });
    crate::pc::respawn(c, aid, unit::CLR_OUTSIGHT);
}
