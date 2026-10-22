//! unit.cpp's skill parts: unit_skilluse_id / unit_skilluse_id2 (a targeted skill: checks, range, cast time,
//! casting packet, the cast-end timer), unit_skilluse_pos / unit_skilluse_pos2 (a ground skill), unit_skillcastcancel,
//! unit_set_castdelay, unit_cancel_combo and unit_addshadowscar (+ unit_shadowscar_timer). unit_set_attackdelay is
//! battle/unit_attack.rs's port (its cast events are read there).

use spacetimedb::{table, Table};

use crate::battle::unit_attack::{unit_set_attackdelay, DELAY_EVENT_CASTBEGIN_ID, DELAY_EVENT_CASTBEGIN_POS, DELAY_EVENT_CASTCANCEL};
use crate::battle::{BCT_ENEMY, BCT_SELF};
use crate::db::skills::*;
use crate::map::{BL_ELEM, BL_HOM, BL_MER, BL_MOB, BL_NPC, BL_PC};
use crate::status::consts::*;
use crate::status::sc;
use crate::timer::{self, Func};
use crate::unit::{self, Unit, USW_FIXPOS};
use crate::world::{Ctx, Tick};

use super::clif::{clif_skillcastcancel, clif_skillcasting, skill_fail, skill_fail_cause};
use super::clif::fail::*;
use super::conf;

/// unit.cpp unit_skilluse_id: casts with the skill's cast time (skill_castfix) and cancel flag.
pub fn unit_skilluse_id(c: &Ctx, src_id: u32, target_id: u32, skill_id: u16, skill_lv: u16) -> i32 {
    let Some(src) = unit::get(c, src_id) else { return 0 };
    let casttime = super::castfix::skill_castfix(c, &src, skill_id, skill_lv);
    unit_skilluse_id2(c, src_id, target_id, skill_id, skill_lv, casttime, skill_get_castcancel(skill_id), false)
}

/// unit.cpp unit_skilluse_pos.
pub fn unit_skilluse_pos(c: &Ctx, src_id: u32, x: i16, y: i16, skill_id: u16, skill_lv: u16) -> i32 {
    let Some(src) = unit::get(c, src_id) else { return 0 };
    let casttime = super::castfix::skill_castfix(c, &src, skill_id, skill_lv);
    unit_skilluse_pos2(c, src_id, x, y, skill_id, skill_lv, casttime, skill_get_castcancel(skill_id), false)
}

/// unit.cpp unit_set_castdelay: the cast time (or the minimum delay) as act delay, plus the security delay that
/// castend removes again.
pub fn unit_set_castdelay(u: &mut Unit, tick: Tick, casttime: i32) {
    // Use casttime or minimum delay, whatever is longer
    let cast_delay = (casttime as i64).max(conf::min_skill_delay_limit());
    // Only apply the cast delay, if it is longer than the act delay (set by unit_set_attackdelay)
    u.ud.canact_tick = u.ud.canact_tick.max(tick + cast_delay);
    // Security delay that will be removed at castend again
    u.ud.canact_tick += super::SECURITY_CASTTIME;
}

/// The SA_FREECAST / LG_EXEEDBREAK speed recalculation while casting (status_calc_bl(sd, SCB_SPEED | SCB_ASPD)).
pub(crate) fn freecast_recalc(c: &Ctx, aid: u32) {
    use crate::status::consts::{SCB_ASPD, SCB_SPEED};
    crate::status::calc_bl(c, aid, (1u64 << SCB_SPEED) | (1u64 << SCB_ASPD), crate::status::defs::SCO_NONE);
}

/// unit.cpp unit_skilluse_id2: checks and starts a targeted skill. 1 success (casting or cast), 0 failure.
#[allow(clippy::too_many_arguments)]
pub fn unit_skilluse_id2(c: &Ctx, src_id: u32, target_id: u32, skill_id: u16, skill_lv: u16, casttime: i32, castcancel: i32, ignore_range: bool) -> i32 {
    let tick = c.tick();
    let Some(src) = unit::get(c, src_id) else { return 0 };
    let mut target_id = target_id;
    let mut casttime = casttime;
    let mut combo = 0;
    if sc::status_isdead(c, &src) {
        return 0; // Do not continue source is dead
    }
    let is_pc = src.kind == BL_PC;
    if src.ud.blockedskill {
        return 0;
    }
    let scl = crate::battle::bl::sc_nonempty(c, &src);
    let inf = skill_get_inf(skill_id);
    let Some(skill) = skill_db().find(skill_id) else { return 0 };
    // temp: used to signal combo-skills right now.
    let combo_sce = scl.as_ref().and_then(|s| s.get(SC_COMBO).cloned());
    if let Some(cs) = combo_sce.as_ref().filter(|_| super::combo::skill_is_combo(skill_id) != 0) {
        let ok = cs.val1 == skill_id as i32 || (is_pc && super::condition::skill_check_condition_castbegin(c, &src, skill_id, skill_lv));
        if ok {
            let ud_target = unit::get(c, src_id).map(|u| u.ud.target).unwrap_or(0);
            let cs = sc::get_sce(c, src_id, SC_COMBO).unwrap_or_else(|| cs.clone());
            if super::combo::skill_is_combo(skill_id) == 2 && target_id == src_id && ud_target > 0 {
                target_id = ud_target;
            } else if cs.val2 != 0 {
                target_id = cs.val2 as u32;
            } else if target_id == src_id || ud_target > 0 {
                target_id = ud_target;
            }
            if inf & INF_SELF_SKILL as i32 != 0 && skill.has_nk(NK_NODAMAGE) {
                target_id = src_id; // exploit fix
            }
            combo = 1;
        } else if target_id == src_id && inf & INF_SELF_SKILL as i32 != 0 && skill.has_inf2(INF2_NOTARGETSELF) {
            target_id = src.ud.target; // Auto-select target. [Skotlex]
            combo = 1;
        }
    } else if target_id == src_id && inf & INF_SELF_SKILL as i32 != 0 && skill.has_inf2(INF2_NOTARGETSELF) {
        target_id = src.ud.target; // Auto-select target. [Skotlex]
        combo = 1;
    }
    let src = unit::get(c, src_id).unwrap_or(src);
    let mut target: Option<Unit> = None;
    if is_pc {
        // Target_id checking.
        if super::condition::skill_is_not_ok(c, &src, skill_id) {
            return 0;
        }
        let scl = crate::battle::bl::sc_nonempty(c, &src);
        match skill_id {
            // Check for skills that auto-select target
            MO_CHAINCOMBO => {
                if let Some(e) = scl.as_ref().and_then(|s| s.get(SC_BLADESTOP)) {
                    match unit::get(c, e.val4 as u32) {
                        Some(t) => target = Some(t),
                        None => return 0,
                    }
                }
            }
            GC_WEAPONCRUSH => {
                if let Some(e) = scl.as_ref().and_then(|s| s.get(SC_WEAPONBLOCK_ON)) {
                    match unit::get(c, e.val1 as u32) {
                        Some(t) => target = Some(t),
                        None => return 0,
                    }
                    combo = 1;
                }
            }
            RL_QD_SHOT => {
                if let Some(e) = scl.as_ref().and_then(|s| s.get(SC_QD_SHOT_READY)) {
                    match unit::get(c, e.val1 as u32) {
                        Some(t) => target = Some(t),
                        None => return 0,
                    }
                    combo = 1;
                }
            }
            WE_MALE | WE_FEMALE => {
                let partner_id = crate::pc::character_of(c, src_id).map(|ch| ch.partner_id).unwrap_or(0);
                if partner_id == 0 {
                    return 0;
                }
                match crate::pc::charid2sd(c, partner_id) {
                    Some(t) => target = Some(t),
                    None => {
                        skill_fail(c, &src, skill_id);
                        return 0;
                    }
                }
            }
            _ => {}
        }
        if let Some(t) = &target {
            target_id = t.id;
        }
    } else if src.kind == BL_HOM {
        match skill_id {
            // Homun-auto-target skills.
            HLIF_HEAL | HLIF_AVOID | HAMI_DEFENCE | HAMI_CASTLE => {
                // battle_get_master never answers nullptr (the homunculus itself without a master)
                let t = crate::battle::target::battle_get_master(c, &src);
                target_id = t.id;
                target = Some(t);
            }
            MH_SONIC_CRAW | MH_TINDER_BREAKER => {
                let skill_id2 = if skill_id == MH_SONIC_CRAW { MH_MIDNIGHT_FRENZY } else { MH_EQC };
                // (sc: nullptr when empty; rAthena dereferences it here, skill_isNotOk_hom's SC_STYLE_CHANGE rule
                // keeps it non-empty in practice)
                let cs = crate::battle::bl::sc_nonempty(c, &src).and_then(|s| s.get(SC_COMBO).cloned());
                if let Some(cs) = cs.filter(|e| e.val1 == skill_id2 as i32) {
                    // It's a combo
                    target_id = cs.val2 as u32;
                    combo = 1;
                    casttime = -1;
                }
            }
            _ => {}
        }
    }
    let target = match target {
        Some(t) => t,
        None => match unit::get(c, target_id) {
            Some(t) => t, // Choose default target
            None => return 0,
        },
    };
    if src.map != target.map || !src.on_map || !target.on_map {
        return 0;
    }
    // (battle_config.ksprotection: 0 — mob_ksprotected is not consulted)
    // Normally not needed because clif.cpp checks for it, but the at/char/script commands don't! [Skotlex]
    if src.ud.skilltimer != 0 && skill_id != SA_CASTCANCEL && skill_id != SO_SPELLFIST {
        return 0;
    }
    if skill.has_inf2(INF2_NOTARGETSELF) && src_id == target_id {
        return 0;
    }
    if !crate::status::check::status_check_skilluse(c, Some(&src), Some(&target), skill_id, 0) {
        return 0;
    }
    // Fail if the targetted skill is near NPC [Cydh]
    if skill.has_inf2(INF2_DISABLENEARNPC) && !ignore_range && super::iface::skill_is_not_ok_npc_range(c, &src, skill_id, skill_lv, target.x, target.y) {
        if is_pc {
            skill_fail(c, &src, skill_id);
        }
        return 0;
    }
    let tstatus = crate::status::status_get_status_data(c, &target);
    // Record the status of the previous skill)
    if is_pc {
        match skill_id {
            SA_CASTCANCEL => {
                if src.ud.skill_id != skill_id {
                    let (id, lv) = (src.ud.skill_id, src.ud.skill_lv);
                    super::state::with(c, src_id, |s| {
                        s.skill_id_old = id;
                        s.skill_lv_old = lv;
                    });
                }
            }
            BD_ENCORE => {
                // Prevent using the dance skill if you no longer have the skill in your tree.
                let dance = super::state::get(c, src_id).skill_id_dance;
                if dance == 0 || crate::skill::checkskill(c, src_id, dance) == 0 {
                    skill_fail(c, &src, skill_id);
                    return 0;
                }
                super::state::with(c, src_id, |s| s.skill_id_old = skill_id);
            }
            BA_PANGVOICE | DC_WINKCHARM => {
                if crate::battle::bl::class_type(c, &target) == crate::status::defs::CLASS_BOSS {
                    skill_fail_cause(c, &src, skill_id, USESKILL_FAIL_TOTARGET);
                    return 0;
                }
            }
            WL_WHITEIMPRISON => {
                if crate::battle::target::battle_check_target(c, &src, &target, BCT_SELF | BCT_ENEMY) < 0 {
                    skill_fail_cause(c, &src, skill_id, USESKILL_FAIL_TOTARGET);
                    return 0;
                }
            }
            MG_FIREBOLT | MG_LIGHTNINGBOLT | MG_COLDBOLT => {
                super::state::with(c, src_id, |s| {
                    s.skill_id_old = skill_id;
                    s.skill_lv_old = skill_lv;
                });
            }
            CR_DEVOTION => {
                if target.kind == BL_PC {
                    let st = super::state::get(c, src_id);
                    let count = (skill_lv as usize).min(super::state::MAX_DEVOTION);
                    if !st.devotion[..count].contains(&target_id) && !st.devotion[..count].contains(&0) {
                        // No free slots, skill Fail
                        skill_fail(c, &src, skill_id);
                        return 0;
                    }
                }
            }
            RL_C_MARKER => {
                let st = super::state::get(c, src_id);
                if !st.c_marker.contains(&target_id) && !st.c_marker.contains(&0) {
                    // No free slots, skill Fail
                    skill_fail(c, &src, skill_id);
                    return 0;
                }
            }
            DK_SERVANT_W_SIGN => {
                let st = super::state::get(c, src_id);
                let count = (skill_lv as usize).min(super::state::MAX_SERVANT_SIGN);
                // Already targetted, or a free slot
                if !st.servant_sign[..count].contains(&target_id) && !st.servant_sign[..count].contains(&0) {
                    // No free slots
                    skill_fail(c, &src, skill_id);
                    return 0;
                }
            }
            TR_RETROSPECTION => {
                // Prevent using the song skill if you no longer have the skill in your tree.
                let song = super::state::get(c, src_id).skill_id_song;
                if song == 0 || crate::skill::checkskill(c, src_id, song) == 0 {
                    skill_fail(c, &src, skill_id);
                    return 0;
                }
                super::state::with(c, src_id, |s| s.skill_id_old = skill_id);
            }
            _ => {}
        }
        if !super::condition::skill_check_condition_castbegin(c, &src, skill_id, skill_lv) {
            return 0;
        }
    }
    let src = unit::get(c, src_id).unwrap_or(src);
    if src.kind == BL_MOB {
        if matches!(skill_id, NPC_SUMMONSLAVE | NPC_SUMMONMONSTER | NPC_DEATHSUMMON | AL_TELEPORT) {
            if let Some(md) = crate::mob::get(c, src_id) {
                if md.master_id != 0 && md.special_ai != 0 {
                    return 0;
                }
            }
        }
    }
    let range = super::misc::skill_get_range2(c, &src, skill_id, skill_lv, true); // Skill cast distance from database
    let mut src = src;
    // New action request received, delete previous action request if not executed yet
    if src.ud.stepaction || src.ud.steptimer != 0 {
        unit::stop_stepaction(c, &mut src);
    }
    // Remember the skill request from the client while walking to the next cell
    if src.kind == BL_PC && src.ud.walktimer != 0 && (!crate::battle::target::battle_check_range(c, &src, &target, range - 1) || ignore_range) {
        src.ud.stepaction = true;
        src.ud.target_to = target_id;
        src.ud.stepskill_id = skill_id;
        src.ud.stepskill_lv = skill_lv;
        unit::save(c, &src);
        return 0; // Attacking will be handled by unit_walktoxy_timer in this case
    }
    unit::save(c, &src);
    // Check range when not using skill on yourself or is a combo-skill during attack
    // (these are supposed to always have the same range as your attack)
    if src.kind != BL_NPC && !ignore_range && src_id != target_id && (combo == 0 || src.ud.attacktimer == 0) {
        if skill_get_state(src.ud.skill_id) == ST_MOVE_ENABLE {
            if !crate::mob::unit::unit_can_reach_bl(c, &src, &target, range + 1, 1).ok() {
                return 0; // Walk-path check failed.
            }
        } else if src.kind == BL_MER && skill_id == MA_REMOVETRAP {
            // Aegis calc remove trap based on Master position, ignoring mercenary O.O (unit.cpp:2359; the mercenary's
            // master: battle_get_master's BL_MER arm, companion.rs)
            let master = crate::battle::target::battle_get_master(c, &src);
            if !crate::battle::target::battle_check_range(c, &master, &target, range + 1) {
                return 0;
            }
        } else if !crate::battle::target::battle_check_range(c, &src, &target, range) {
            return 0; // Arrow-path check failed.
        }
    }
    if combo == 0 {
        // Stop attack on non-combo skills [Skotlex]
        unit::stop_attack(c, &mut src);
        unit::save(c, &src);
    }
    src.ud.skillcastcancel = castcancel != 0;
    unit::save(c, &src);
    // Combo: Used to signal force cast now.
    combo = 0;
    let scl = crate::battle::bl::sc_nonempty(c, &src);
    // `sd` from here on (the elementals' arm below reassigns it to the elemental's master)
    let mut sd_id = is_pc.then_some(src_id);
    match skill_id {
        ALL_RESURRECTION => {
            if sc::battle_check_undead(tstatus.race as i32, tstatus.def_ele as i32) {
                combo = 1;
            } else if !sc::status_isdead(c, &target) {
                return 0; // Can't cast on non-dead characters.
            }
        }
        MO_EXTREMITYFIST => {
            if let Some(e) = scl.as_ref().and_then(|s| s.get(SC_COMBO)) {
                if e.val1 == MO_COMBOFINISH as i32 || e.val1 == CH_CHAINCRUSH as i32 {
                    casttime = -1;
                }
            }
            combo = 1;
        }
        SR_GATEOFHELL | SR_TIGERCANNON => {
            if let Some(e) = scl.as_ref().and_then(|s| s.get(SC_COMBO)) {
                if e.val1 == SR_FALLENEMPIRE as i32 {
                    casttime = -1;
                }
            }
            combo = 1;
        }
        SA_SPELLBREAKER => combo = 1,
        TK_RUN => {
            if scl.as_ref().map(|s| s.has(SC_RUN)).unwrap_or(false) {
                casttime = -1;
            }
        }
        GD_EMERGENCYCALL => {
            // Emergency Call double cast when the user has learned Leap [Daegaladh]
            if is_pc && (crate::skill::checkskill(c, src_id, TK_HIGHJUMP) != 0 || crate::skill::checkskill(c, src_id, SU_LOPE) >= 3) {
                casttime *= 2;
            }
        }
        RA_WUGDASH => {
            if scl.as_ref().map(|s| s.has(SC_WUGDASH)).unwrap_or(false) {
                casttime = -1;
            }
        }
        DK_SERVANT_W_PHANTOM => {
            // Stops servants from being consumed on unmarked targets.
            // Only allow to attack if the enemy has a sign mark given by the caster.
            let marked = sc::get_sce(c, target.id, SC_SERVANT_SIGN).map(|e| e.val1 == src_id as i32).unwrap_or(false);
            if !marked {
                if is_pc {
                    skill_fail_cause(c, &src, skill_id, USESKILL_FAIL);
                }
                return 0;
            }
        }
        EL_WIND_SLASH | EL_HURRICANE | EL_TYPOON_MIS | EL_STONE_HAMMER | EL_ROCK_CRUSHER | EL_STONE_RAIN | EL_ICE_NEEDLE | EL_WATER_SCREW | EL_TIDAL_WEAPON => {
            if src.kind == BL_ELEM {
                // sd = BL_CAST(BL_PC, battle_get_master(src)): kept as `sd` for the rest of the function (the
                // cast-sensor event, Free Cast's recalculation and canlog_tick below then read the master)
                let m = crate::battle::target::battle_get_master(c, &src);
                sd_id = (m.kind == BL_PC).then_some(m.id);
                if let Some(aid) = sd_id {
                    // the master ordered the attack (SO_EL_ACTION): no cast time
                    if super::state::get(c, aid).skill_id_old == SO_EL_ACTION {
                        casttime = -1;
                        super::state::with(c, aid, |s| s.skill_id_old = 0);
                    }
                }
            }
        }
        _ => {}
    }
    // (`combo` is read after this point only by the pre-renewal cast-sensor rule `casttime > 0 || combo > 0`)
    let _ = combo;
    // Moved here to prevent Suffragium from ending if skill fails (RENEWAL_CAST)
    let casttime = super::castfix::skill_vfcastfix(c, &src, casttime as f64, skill_id, skill_lv);
    let mut src = unit::get(c, src_id).unwrap_or(src);
    // Need TK_RUN or WUGDASH handler to be done before that, see bugreport:6026
    if !src.ud.running {
        // Even though this is not how official works but this will do the trick. bugreport:6829
        unit::stop_walking(c, &mut src, USW_FIXPOS, 0);
        unit::save(c, &src);
    }
    // In official this is triggered even if no cast time.
    clif_skillcasting(c, &src, Some(&target), 0, 0, skill_id, skill_lv, skill_get_ele(skill_id, skill_lv), casttime);
    if sd_id.is_some() && target.kind == BL_MOB {
        // (RENEWAL: the event runs even without cast time)
        crate::mob::skill::mobskill_event(c, target.id, &src, tick, -1, 0); // Cast targetted skill event.
        use crate::db::mobs::{MD_CASTSENSORCHASE, MD_CASTSENSORIDLE};
        use crate::status::defs::MD_ANGRY;
        // rAthena's tstatus points at the live status: the event above may have changed the monster's mode (e.g.
        // NPC_EMOTION's val[1] / val[2], skills/npc), so the mode is read again after it (unit.cpp:2497-2513)
        let tmode = crate::unit::get(c, target.id).map(|t| crate::status::status_get_status_data(c, &t).mode).unwrap_or(tstatus.mode);
        let has = |m| crate::battle::mobif::has_mode(tmode, m as i32);
        if (has(MD_CASTSENSORIDLE) || has(MD_CASTSENSORCHASE)) && crate::battle::target::battle_check_target(c, &target, &src, BCT_ENEMY) > 0 && !ignore_range {
            if let Some(mut md) = crate::mob::get(c, target.id) {
                use crate::battle::mobif::{MSS_IDLE, MSS_RUSH};
                const MSS_FOLLOW: i32 = 7;
                const MSS_WALK: i32 = 1;
                let st = md.skillstate;
                let mut changed = false;
                if (st == MSS_RUSH || st == MSS_FOLLOW) && has(MD_CASTSENSORCHASE) {
                    changed = true;
                }
                if (st == MSS_IDLE || st == MSS_WALK) && has(MD_CASTSENSORIDLE) {
                    changed = true;
                }
                if changed {
                    md.target_id = src_id;
                    md.aggressive = has(MD_ANGRY);
                    crate::mob::put(c, &md);
                }
            }
        }
    }
    let mut src = unit::get(c, src_id).unwrap_or(src);
    if casttime <= 0 {
        src.ud.skillcastcancel = false;
    }
    if let Some(sd_aid) = sd_id.filter(|_| skill_id == CG_ARROWVULCAN) {
        super::state::with(c, sd_aid, |s| s.canequip_tick = tick + casttime as i64);
    }
    src.ud.skilltarget = target_id;
    src.ud.skillx = 0;
    src.ud.skilly = 0;
    src.ud.skill_id = skill_id;
    src.ud.skill_lv = skill_lv;
    // Set attack and act delays
    // Please note that the call below relies on ud->skill_id being set!
    unit_set_attackdelay(c, &mut src, tick, DELAY_EVENT_CASTBEGIN_ID);
    // Apply cast time and general delays
    unit_set_castdelay(&mut src, tick, if skill_get_cast(skill_id, skill_lv) != 0 { casttime } else { 0 });
    unit::save(c, &src);
    // These 3 status do not stack, so it's efficient to use if-else
    if let Some(s) = crate::battle::bl::sc_nonempty(c, &src) {
        if s.get(SC_CLOAKING).map(|e| e.val4 & 4 == 0).unwrap_or(false) && skill_id != AS_CLOAKING && skill_id != SHC_SHADOW_STAB {
            sc::status_change_end(c, src_id, SC_CLOAKING, 0);
            if !unit::get(c, src_id).map(|u| u.on_map).unwrap_or(false) {
                return 0; // Warped away!
            }
        } else if s.get(SC_CLOAKINGEXCEED).map(|e| e.val4 & 4 == 0).unwrap_or(false) && skill_id != GC_CLOAKINGEXCEED && skill_id != SHC_SHADOW_STAB && skill_id != SHC_SAVAGE_IMPACT {
            sc::status_change_end(c, src_id, SC_CLOAKINGEXCEED, 0);
            if !unit::get(c, src_id).map(|u| u.on_map).unwrap_or(false) {
                return 0;
            }
        } else if s.has(SC_NEWMOON) && skill_id != SJ_NEWMOONKICK {
            sc::status_change_end(c, src_id, SC_NEWMOON, 0);
            if !unit::get(c, src_id).map(|u| u.on_map).unwrap_or(false) {
                return 0; // Warped away!
            }
        }
    }
    if casttime > 0 {
        let mut s = unit::get(c, src_id).unwrap_or(src);
        s.ud.skilltimer = timer::add(c, tick + casttime as i64, Func::SkillCastendId, src_id, 0);
        unit::save(c, &s);
        if let Some(sd_aid) = sd_id.filter(|&a| crate::skill::checkskill(c, a, SA_FREECAST) > 0 || skill_id == LG_EXEEDBREAK) {
            freecast_recalc(c, sd_aid);
        }
    } else {
        let tid = unit::get(c, src_id).map(|u| u.ud.skilltimer).unwrap_or(0);
        super::castend::skill_castend_id(c, tid, tick, src_id, 0);
    }
    if let Some(sd_aid) = sd_id.filter(|_| conf::prevent_logout_trigger() & conf::PLT_SKILL != 0) {
        if let Some(mut p) = crate::pc::get(c, sd_aid) {
            p.canlog_tick = c.tick();
            crate::pc::put(c, &p);
        }
    }
    1
}

/// unit.cpp unit_skilluse_pos2: checks and starts a ground skill. 1 success, 0 failure.
#[allow(clippy::too_many_arguments)]
pub fn unit_skilluse_pos2(c: &Ctx, src_id: u32, skill_x: i16, skill_y: i16, skill_id: u16, skill_lv: u16, casttime: i32, castcancel: i32, ignore_range: bool) -> i32 {
    let tick = c.tick();
    let Some(src) = unit::get(c, src_id) else { return 0 };
    if !src.on_map {
        return 0; // Not on the map
    }
    if sc::status_isdead(c, &src) {
        return 0;
    }
    let is_pc = src.kind == BL_PC;
    if src.ud.blockedskill {
        return 0;
    }
    if src.ud.skilltimer != 0 {
        // Normally not needed since clif.cpp checks for it, but at/char/script commands don't! [Skotlex]
        return 0;
    }
    if !skill_db().exists(skill_id) {
        return 0;
    }
    if is_pc {
        if super::condition::skill_is_not_ok(c, &src, skill_id) || !super::condition::skill_check_condition_castbegin(c, &src, skill_id, skill_lv) {
            return 0;
        }
        if skill_id == MG_FIREWALL {
            let s = unit::get(c, src_id).unwrap_or_else(|| src.clone());
            if !super::iface::skill_pos_maxcount_check(c, &s, skill_x, skill_y, skill_id, skill_lv, BL_PC, true) {
                return 0; // Special check for Firewall only
            }
        }
    }
    let src = unit::get(c, src_id).unwrap_or(src);
    if (SC_MANHOLE..=SC_FEINTBOMB).contains(&skill_id) && super::iface::map_getcell_maelstrom(c, src.map, skill_x, skill_y) {
        if is_pc {
            skill_fail(c, &src, skill_id);
        }
        return 0;
    }
    if !crate::status::check::status_check_skilluse(c, Some(&src), None, skill_id, 0) {
        return 0;
    }
    // Fail if the targetted skill is near NPC [Cydh]
    if skill_get_inf2(skill_id, &[INF2_DISABLENEARNPC]) && !ignore_range && super::iface::skill_is_not_ok_npc_range(c, &src, skill_id, skill_lv, skill_x, skill_y) {
        if is_pc {
            skill_fail(c, &src, skill_id);
        }
        return 0;
    }
    // Check range and obstacle: a BL_NUL block_list at (skill_x, skill_y)
    let mut bl = src.clone();
    bl.id = 0;
    bl.kind = crate::map::BL_NUL;
    bl.x = skill_x;
    bl.y = skill_y;
    let range = if src.kind == BL_NPC {
        crate::conf::area_size() as i32 // NPC-objects can override cast distance: maximum visible distance
    } else {
        super::misc::skill_get_range2(c, &src, skill_id, skill_lv, true) // Skill cast distance from database
    };
    let mut src = src;
    // New action request received, delete previous action request if not executed yet
    if src.ud.stepaction || src.ud.steptimer != 0 {
        unit::stop_stepaction(c, &mut src);
    }
    // Remember the skill request from the client while walking to the next cell
    if src.kind == BL_PC && src.ud.walktimer != 0 && (!crate::battle::target::battle_check_range(c, &src, &bl, range - 1) || ignore_range) {
        let xs = crate::data::maps().get(src.map).map(|m| m.xs as i32).unwrap_or(0);
        // Convert coordinates to target_to so we can use it as target later
        src.ud.stepaction = true;
        src.ud.target_to = (skill_x as i32 + skill_y as i32 * xs) as u32;
        src.ud.stepskill_id = skill_id;
        src.ud.stepskill_lv = skill_lv;
        unit::save(c, &src);
        return 0; // Attacking will be handled by unit_walktoxy_timer in this case
    }
    unit::save(c, &src);
    if !ignore_range {
        if skill_get_state(src.ud.skill_id) == ST_MOVE_ENABLE {
            if !crate::mob::unit::unit_can_reach_bl(c, &src, &bl, range + 1, 1).ok() {
                return 0; // Walk-path check failed.
            }
        } else if !crate::battle::target::battle_check_range(c, &src, &bl, range) {
            return 0; // Arrow-path check failed.
        }
    }
    unit::stop_attack(c, &mut src);
    unit::save(c, &src);
    // Moved here to prevent Suffragium from ending if skill fails (RENEWAL_CAST)
    let casttime = super::castfix::skill_vfcastfix(c, &src, casttime as f64, skill_id, skill_lv);
    let mut src = unit::get(c, src_id).unwrap_or(src);
    src.ud.skillcastcancel = castcancel != 0 && casttime > 0;
    src.ud.skill_id = skill_id;
    src.ud.skill_lv = skill_lv;
    src.ud.skillx = skill_x;
    src.ud.skilly = skill_y;
    src.ud.skilltarget = 0;
    // Set attack and act delays
    // Please note that the call below relies on ud->skill_id being set!
    unit_set_attackdelay(c, &mut src, tick, DELAY_EVENT_CASTBEGIN_POS);
    // Apply cast time and general delays
    unit_set_castdelay(&mut src, tick, if skill_get_cast(skill_id, skill_lv) != 0 { casttime } else { 0 });
    unit::save(c, &src);
    // These 3 status do not stack, so it's efficient to use if-else
    if let Some(s) = crate::battle::bl::sc_nonempty(c, &src) {
        if s.get(SC_CLOAKING).map(|e| e.val4 & 4 == 0).unwrap_or(false) {
            sc::status_change_end(c, src_id, SC_CLOAKING, 0);
            if !unit::get(c, src_id).map(|u| u.on_map).unwrap_or(false) {
                return 0; // Warped away!
            }
        } else if s.get(SC_CLOAKINGEXCEED).map(|e| e.val4 & 4 == 0).unwrap_or(false) {
            sc::status_change_end(c, src_id, SC_CLOAKINGEXCEED, 0);
            if !unit::get(c, src_id).map(|u| u.on_map).unwrap_or(false) {
                return 0;
            }
        } else if s.has(SC_NEWMOON) {
            sc::status_change_end(c, src_id, SC_NEWMOON, 0);
            if !unit::get(c, src_id).map(|u| u.on_map).unwrap_or(false) {
                return 0;
            }
        }
    }
    let mut src = unit::get(c, src_id).unwrap_or(src);
    unit::stop_walking(c, &mut src, USW_FIXPOS, 0);
    unit::save(c, &src);
    // In official this is triggered even if no cast time.
    clif_skillcasting(c, &src, None, skill_x as u16, skill_y as u16, skill_id, skill_lv, skill_get_ele(skill_id, skill_lv), casttime);
    if casttime > 0 {
        let mut s = unit::get(c, src_id).unwrap_or(src);
        s.ud.skilltimer = timer::add(c, tick + casttime as i64, Func::SkillCastendPos, src_id, 0);
        unit::save(c, &s);
        if (is_pc && crate::skill::checkskill(c, src_id, SA_FREECAST) > 0) || skill_id == LG_EXEEDBREAK {
            if is_pc {
                freecast_recalc(c, src_id);
            }
        }
    } else {
        let mut s = unit::get(c, src_id).unwrap_or(src);
        s.ud.skilltimer = 0;
        unit::save(c, &s);
        super::castend::skill_castend_pos(c, 0, tick, src_id, 0);
    }
    if is_pc && conf::prevent_logout_trigger() & conf::PLT_SKILL != 0 {
        if let Some(mut p) = crate::pc::get(c, src_id) {
            p.canlog_tick = c.tick();
            crate::pc::put(c, &p);
        }
    }
    1
}

/// unit.cpp unit_skillcastcancel: cancels a cast (type &1: the skill of sd->skill_id_old (Cast Cancel), &2: only if
/// it can be cancelled by damage). 1 when a cast was cancelled.
pub fn unit_skillcastcancel(c: &Ctx, bl_id: u32, type_: i32) -> i32 {
    let tick = c.tick();
    let Some(mut u) = unit::get(c, bl_id) else { return 0 };
    if u.ud.skilltimer == 0 {
        return 0; // Nothing to cancel.
    }
    let is_pc = u.kind == BL_PC;
    if type_ & 2 != 0 {
        // See if it can be cancelled.
        if !u.ud.skillcastcancel {
            return 0;
        }
        if is_pc {
            if let Some(sd) = crate::status::data::sd_get(c, bl_id) {
                let humming = sc::has_sce(c, bl_id, SC_UNLIMITEDHUMMINGVOICE);
                if sd.special_state.no_castcancel2
                    || ((humming || sd.special_state.no_castcancel) && !crate::npc::mapflag_gvg2(c, u.map) && !crate::npc::mapflag(c, u.map, crate::npc::MF_BATTLEGROUND))
                {
                    // fixed flags being read the wrong way around [blackhole89]
                    return 0;
                }
            }
        }
    }
    u.ud.canact_tick = tick;
    let skill_id = if type_ & 1 != 0 && is_pc { super::state::get(c, bl_id).skill_id_old } else { u.ud.skill_id };
    let ret = if skill_get_inf(skill_id) & INF_GROUND_SKILL as i32 != 0 {
        timer::delete_checked(c, u.ud.skilltimer, Func::SkillCastendPos)
    } else {
        timer::delete_checked(c, u.ud.skilltimer, Func::SkillCastendId)
    };
    if ret < 0 {
        log::error!("delete timer error : skill_id : {ret}");
    }
    u.ud.skilltimer = 0;
    unit::save(c, &u);
    if is_pc && (crate::skill::checkskill(c, bl_id, SA_FREECAST) > 0 || skill_id == LG_EXEEDBREAK) {
        freecast_recalc(c, bl_id);
    }
    if is_pc && skill_id == CG_ARROWVULCAN {
        super::state::with(c, bl_id, |s| s.canequip_tick = tick);
    }
    let mut u = unit::get(c, bl_id).unwrap_or(u);
    unit_set_attackdelay(c, &mut u, tick, DELAY_EVENT_CASTCANCEL);
    unit::save(c, &u);
    if u.kind == BL_MOB {
        if let Some(mut md) = crate::mob::get(c, bl_id) {
            md.skill_idx = -1;
            crate::mob::put(c, &md);
        }
    }
    clif_skillcastcancel(c, &u);
    1
}

/// unit.cpp unit_cancel_combo: ends SC_COMBO and restarts a waiting auto-attack after the attack motion.
pub fn unit_cancel_combo(c: &Ctx, bl_id: u32) -> i32 {
    if sc::status_change_end(c, bl_id, SC_COMBO, 0) == 0 {
        return 0; // Combo wasn't active.
    }
    let Some(mut u) = unit::get(c, bl_id) else { return 0 };
    let amotion = crate::status::status_get_status_data(c, &u).amotion as Tick;
    u.ud.attackabletime = c.tick() + amotion;
    if u.ud.attacktimer == 0 {
        unit::save(c, &u);
        return 1; // Nothing more to do.
    }
    timer::delete(c, u.ud.attacktimer);
    u.ud.attacktimer = timer::add(c, u.ud.attackabletime, Func::UnitAttack, bl_id, 0);
    unit::save(c, &u);
    1
}

/// unit.cpp unit_changetarget_sub for the unit `bl_id` (HAMI_CASTLE's caller): crate::unit::changetarget_sub on its
/// row, saved.
pub fn unit_changetarget_sub(c: &Ctx, bl_id: u32, target: &Unit) {
    let Some(mut u) = unit::get(c, bl_id) else { return };
    unit::changetarget_sub(c, &mut u, target);
    unit::save(c, &u);
}

// ---------------------------------------------------------------------------------------------- shadow scars

/// unit.cpp MAX_SHADOW_SCAR.
pub const MAX_SHADOW_SCAR: usize = 100;

/// ud->shadow_scar_timer (the timers of a unit's Shadow Scars, in the order they were added).
#[table(accessor = unit_shadow_scar)]
#[derive(Clone, Debug)]
pub struct UnitShadowScar {
    #[primary_key]
    pub bl_id: u32,
    pub timers: Vec<u64>,
}

/// clif.cpp clif_enchantingshadow_spirit: ZC_TARGET_SPIRITS (PACKETVER_RE_NUM >= 20191120) to the area.
fn clif_enchantingshadow_spirit(c: &Ctx, bl: &Unit, amount: usize) {
    use crate::net::packets::PACKET_ZC_TARGET_SPIRITS;
    let p = PACKET_ZC_TARGET_SPIRITS { packetType: PACKET_ZC_TARGET_SPIRITS::ID as i16, GID: bl.id, unknown_val: 0, amount: amount as u16 };
    crate::clif::send(c, &p.to_vec(), bl, crate::clif::Target::Area);
}

/// unit.cpp unit_addshadowscar: a Shadow Scar for `interval` ms.
pub fn unit_addshadowscar(c: &Ctx, bl_id: u32, interval: i32) {
    let Some(u) = unit::get(c, bl_id) else { return };
    let mut row = c.db().unit_shadow_scar().bl_id().find(bl_id).unwrap_or(UnitShadowScar { bl_id, timers: Vec::new() });
    if row.timers.len() >= MAX_SHADOW_SCAR {
        log::warn!("unit_addshadowscar: Unit {} ({bl_id}) has reached the maximum amount of Shadow Scars ({MAX_SHADOW_SCAR}).", u.name);
        return;
    }
    row.timers.push(timer::add(c, c.tick() + interval as i64, Func::UnitShadowScar, bl_id, 0));
    let n = row.timers.len();
    if c.db().unit_shadow_scar().bl_id().find(bl_id).is_some() {
        c.db().unit_shadow_scar().bl_id().update(row);
    } else {
        c.db().unit_shadow_scar().insert(row);
    }
    if crate::battle::mobif::has_sc(u.kind) {
        if let Some(mut e) = sc::get_sce(c, bl_id, SC_SHADOW_SCAR) {
            e.val1 = n as i32;
            sc::put_sce(c, &e);
        } else {
            sc::sc_start(c, Some(&u), &u, SC_SHADOW_SCAR, 100, 1, sc::INFINITE_TICK);
        }
        let u = unit::get(c, bl_id).unwrap_or(u);
        clif_enchantingshadow_spirit(c, &u, n);
    }
}

/// unit.cpp unit_shadowscar_timer (Func::UnitShadowScar).
pub fn unit_shadowscar_timer(c: &Ctx, tid: u64, bl_id: u32) {
    if unit::get(c, bl_id).is_none() {
        return;
    }
    let Some(mut row) = c.db().unit_shadow_scar().bl_id().find(bl_id) else { return };
    if let Some(i) = row.timers.iter().position(|&t| t == tid) {
        row.timers.remove(i);
    }
    let empty = row.timers.is_empty();
    c.db().unit_shadow_scar().bl_id().update(row);
    if empty {
        sc::status_change_end(c, bl_id, SC_SHADOW_SCAR, 0);
    }
}

/// unit_free: the unit's shadow scar row goes with it.
pub fn free_unit(c: &Ctx, bl_id: u32) {
    c.db().unit_shadow_scar().bl_id().delete(bl_id);
}

// the helper constants some callers of this module use
pub use crate::map::BL_NUL;
#[allow(unused_imports)]
use BL_HOM as _;

// unit.cpp unit_escape and map.cpp map_delblock, ported in the npc class files that first needed them
// (NPC_RUN / NPC_RANDOMMOVE, NPC_SELFDESTRUCTION); reachable here for every family.
#[allow(unused_imports)]
pub(crate) use crate::skills::npc::npcrun::unit_escape;
#[allow(unused_imports)]
pub(crate) use crate::skills::npc::suicidebombing::map_delblock;
