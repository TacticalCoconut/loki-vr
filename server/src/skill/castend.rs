//! skill.cpp's cast-end side: the timers skill_castend_id / skill_castend_pos (run when the cast time is over, or
//! at once for an instant cast), skill_castend_id_check, skill_keep_using, and the dispatch skill_castend_damage_id /
//! skill_castend_nodamage_id (their shells, the case arms still in skill.cpp — the normal attack's splash, the new
//! guild skills, the wide NPC status skills — and `skill->impl->…` for the rest). skill_castend_pos2's shell is the
//! skillunit agent's (skill/iface.rs until merged).

use crate::battle::{BCT_ENEMY, BCT_GUILD, BCT_NEUTRAL, BCT_NOENEMY, BCT_PARTY, DMG_SPLASH};
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_MER, BL_HOM, BL_MOB, BL_PC, BL_SKILL};
use crate::status::consts::*;
use crate::status::sc;
use crate::timer::{self, Func};
use crate::unit::{self, Unit, USW_FIXPOS};
use crate::world::{Ctx, Tick};

use super::area::{self, foreach_in_range, Wall};
use super::clif::fail::*;
use super::clif::{clif_skill_damage, clif_skill_fail, clif_skill_nodamage, skill_fail};
use super::{conf, SD_LEVEL, SD_PREAMBLE, SECURITY_CASTTIME, SKILL_NOCONSUME_REQ};
use crate::status::consts::SC_NONE;

/// mob.hpp MOBID_GUILD_SKILL_FLAG / MOBID_EMPERIUM.
pub const MOBID_GUILD_SKILL_FLAG: i32 = 20269;
pub const MOBID_EMPERIUM: i32 = 1288;
/// mob.hpp e_mob_skill_condition MSC_SKILLUSED.
pub const MSC_SKILLUSED: i32 = 16;

fn has_mode(c: &Ctx, u: &Unit, m: i32) -> bool {
    crate::battle::bl::has_mode(c, u, m)
}

/// skill.cpp skill_castend_id_check: -1 when the skill may go on, else the useskill_fail_cause (USESKILL_FAIL_MAX:
/// fail without a message).
fn skill_castend_id_check(c: &Ctx, src: &Unit, target: &Unit, skill_id: u16, skill_lv: u16) -> i32 {
    let Some(skill) = skill_db().find(skill_id) else { return USESKILL_FAIL_MAX as i32 };
    let mut inf = skill.inf as i32;
    let tsc = crate::battle::bl::sc(c, target);
    let is_emperium = crate::battle::bl::class_of(c, target) == MOBID_EMPERIUM;
    if src.id != target.id && (has_mode(c, target, crate::status::defs::MD_SKILLIMMUNE) || (is_emperium && !skill.has_inf2(INF2_TARGETEMPERIUM))) && skill_get_casttype(skill_id) == CastType::NoDamage {
        return USESKILL_FAIL_MAX as i32; // Don't show a skill fail message (NoDamage type doesn't consume requirements)
    }
    match skill_id {
        AL_INCAGI | AL_DECAGI | SA_DISPELL => {
            // Mado Gear is immune to Dispell according to bugreport:49 [Ind]
            if tsc.is_some() && target.sc.option & crate::status::defs::OPTION_MADOGEAR != 0 {
                return USESKILL_FAIL_TOTARGET as i32;
            }
        }
        RG_BACKSTAP => {
            // (RENEWAL: no direction check)
            if crate::path::check_distance((src.x - target.x) as i32, (src.y - target.y) as i32, 0) {
                return USESKILL_FAIL_MAX as i32;
            }
        }
        RG_STEALCOIN => {
            // Does not work on non-monsters, bosses and targets already mugged
            if target.kind != BL_MOB || has_mode(c, target, crate::status::defs::MD_STATUSIMMUNE) || crate::mob::get(c, target.id).map(|m| m.steal_coin_flag).unwrap_or(false) {
                return USESKILL_FAIL as i32;
            }
        }
        PR_TURNUNDEAD => {
            let ts = crate::battle::bl::status(c, target);
            if !sc::battle_check_undead(ts.race as i32, ts.def_ele as i32) {
                return USESKILL_FAIL_MAX as i32;
            }
        }
        DC_WINKCHARM => {
            use crate::status::defs::{RC_ANGEL, RC_DEMIHUMAN, RC_DEMON, RC_PLAYER_DORAM, RC_PLAYER_HUMAN};
            match crate::battle::bl::status(c, target).race as i32 {
                RC_DEMON | RC_DEMIHUMAN | RC_ANGEL | RC_PLAYER_HUMAN | RC_PLAYER_DORAM => {}
                _ => return USESKILL_FAIL_LEVEL as i32,
            }
        }
        PR_LEXDIVINA | MER_LEXDIVINA => {
            // If it's not an enemy, and not silenced, you can't use the skill on them. [Skotlex]
            if crate::battle::target::battle_check_target(c, src, target, BCT_ENEMY) <= 0 && !tsc.as_ref().map(|s| s.has(SC_SILENCE)).unwrap_or(false) {
                return USESKILL_FAIL_LEVEL as i32;
            }
            return -1; // Works on silenced allies
        }
        PF_SOULBURN => {
            // Only works on players
            if target.kind != BL_PC {
                return USESKILL_FAIL_LEVEL as i32;
            }
        }
        RA_WUGSTRIKE => {
            // Check if path can be reached
            if crate::path::path_search(src.map, src.x, src.y, target.x, target.y, 1, crate::map::CellChk::ChkNoReach).is_none() {
                return USESKILL_FAIL_MAX as i32;
            }
        }
        MG_NAPALMBEAT | MG_FIREBALL | HT_BLITZBEAT | AS_GRIMTOOTH | MO_COMBOFINISH | NC_VULCANARM | SR_TIGERCANNON => {
            // These can damage traps, but can't target traps directly
            if target.kind == BL_SKILL && super::iface::skill_unit_target_is_trap_or_groupless(c, target) {
                return USESKILL_FAIL_MAX as i32;
            }
        }
        IQ_SECOND_FLAME | IQ_SECOND_FAITH | IQ_SECOND_JUDGEMENT => {
            if !tsc.as_ref().map(|s| s.has(SC_FIRST_BRAND) || s.has(SC_SECOND_BRAND)).unwrap_or(false) {
                return USESKILL_FAIL_LEVEL as i32;
            }
        }
        IQ_THIRD_PUNISH | IQ_THIRD_FLAME_BOMB | IQ_THIRD_CONSECRATION => {
            if !tsc.as_ref().map(|s| s.has(SC_SECOND_BRAND)).unwrap_or(false) {
                return USESKILL_FAIL_LEVEL as i32;
            }
        }
        SH_BLESSING_OF_MYSTICAL_CREATURES => {
            if src.id == target.id {
                return USESKILL_FAIL_TOTARGET as i32;
            }
            if crate::battle::target::battle_check_target(c, src, target, BCT_PARTY) <= 0 {
                return USESKILL_FAIL_TOTARGET as i32;
            }
            // status_get_class_(target) is status_data::class_ (CLASS_NORMAL .. CLASS_ALL), not the job's MAPID: rAthena's
            // test never matches a Doram; kept as it is
            let tclass = crate::battle::bl::status(c, target).class_ as u64;
            if tclass & crate::db::jobs::MAPID_FIRSTMASK as u64 == crate::db::jobs::MAPID_SUMMONER as u64 {
                return USESKILL_FAIL_TOTARGET as i32;
            }
            if tsc.as_ref().map(|s| s.has(SC_BLESSING_OF_M_C_DEBUFF)).unwrap_or(false) {
                return USESKILL_FAIL_TOTARGET as i32;
            }
        }
        SKE_STAR_BURST | SKE_STAR_CANNON => {
            // a Twinkling Galaxy unit of the caster in range (skill units: skillunit agent)
            if !super::iface::caster_has_unit_in_range(c, src, SKE_TWINKLING_GALAXY) {
                return USESKILL_FAIL_NEED_TWINKLING_GALAXY as i32;
            }
        }
        _ => {}
    }
    if inf & INF_ATTACK_SKILL as i32 != 0 || (inf & INF_SELF_SKILL as i32 != 0 && skill.has_inf2(INF2_NOTARGETSELF)) {
        inf = BCT_ENEMY; // Offensive skill. (Combo skills: casted through combo.)
    } else if skill.has_inf2(INF2_NOTARGETENEMY) {
        inf = BCT_NOENEMY;
    } else {
        inf = 0;
    }
    if (skill.has_inf2(INF2_PARTYONLY) || skill.has_inf2(INF2_GUILDONLY)) && src.id != target.id {
        inf |= (if skill.has_inf2(INF2_PARTYONLY) { BCT_PARTY } else { 0 }) | (if skill.has_inf2(INF2_GUILDONLY) { BCT_GUILD } else { 0 });
        // Remove neutral targets (but allow enemy if skill is designed to be so)
        inf &= !BCT_NEUTRAL;
    }
    // Cannot be casted to Emperium
    if matches!(skill_id, WZ_ESTIMATION | SL_SKE | SL_SKA | RK_PHANTOMTHRUST | NPC_PHANTOMTHRUST) && target.kind == BL_MOB && crate::mob::get(c, target.id).map(|m| m.mob_id == MOBID_EMPERIUM).unwrap_or(false) {
        return USESKILL_FAIL_MAX as i32;
    }
    if inf != 0 && crate::battle::target::battle_check_target(c, src, target, inf) <= 0 {
        return match skill_id {
            RK_PHANTOMTHRUST | NPC_PHANTOMTHRUST | AB_CLEARANCE => USESKILL_FAIL_TOTARGET as i32,
            _ => USESKILL_FAIL_LEVEL as i32,
        };
    }
    // Fogwall makes all offensive-type targetted skills fail at 75%
    // Jump Kick can still fail even though you can jump to friendly targets.
    if (inf & BCT_ENEMY != 0 || skill_id == TK_JUMPKICK || skill_id == EM_PSYCHIC_STREAM) && tsc.as_ref().map(|s| s.has(SC_FOGWALL)).unwrap_or(false) && c.rnd() % 100 < 75 {
        return USESKILL_FAIL_LEVEL as i32;
    }
    let _ = skill_lv;
    -1
}

/// skill.cpp skill_keep_using (Func::SkillKeepUsing): the client's "keep using" request is repeated.
pub fn skill_keep_using(c: &Ctx, _tid: u64, aid: u32) {
    let st = super::state::get(c, aid);
    if crate::pc::get(c, aid).is_some() && st.keep_using_skill_id != 0 {
        super::state::with(c, aid, |s| s.keep_using_tid = 0);
        super::parse::clif_parse_skill_toid(c, aid, st.keep_using_skill_id, st.keep_using_level, st.keep_using_target);
    }
}

/// map.cpp map_random_dir: a random walkable cell as far from `bl` as (x, y) is; None after 100 tries.
pub fn map_random_dir(c: &Ctx, bl: &Unit, x: i16, y: i16) -> Option<(i16, i16)> {
    use crate::path::{DIRX, DIRY};
    let mut xi = x.wrapping_sub(bl.x);
    let mut yi = y.wrapping_sub(bl.y);
    let dist2: i32 = xi as i32 * xi as i32 + yi as i32 * yi as i32;
    let mut dist = (dist2 as f32).sqrt() as i16;
    if dist < 1 {
        dist = 1;
    }
    let mut i: i16 = 0;
    loop {
        let j = (1 + 2 * c.rnd_value(0, 3)) as usize; // Pick a random diagonal direction
        let mut segment = c.rnd_value(1, dist as i32) as i16; // Pick a random interval from the whole vector in that direction
        xi = bl.x + segment * DIRX[j];
        segment = ((dist2 - segment as i32 * segment as i32) as f32).sqrt() as i16; // The complement of the previously picked segment
        yi = bl.y + segment * DIRY[j];
        let blocked = crate::map::getcell(c, bl.map, xi, yi, crate::map::CellChk::ChkNoPass)
            || crate::path::path_search(bl.map, bl.x, bl.y, xi, yi, 1, crate::map::CellChk::ChkNoReach).is_none();
        if !blocked {
            break;
        }
        i += 1;
        if i >= 100 {
            break;
        }
    }
    if i < 100 {
        Some((xi, yi))
    } else {
        None
    }
}

/// clif.cpp clif_callpartner: ZC_COUPLENAME with the partner's name (empty without one) to the area.
fn clif_callpartner(c: &Ctx, sd: &Unit) {
    use crate::net::packets::PACKET_ZC_COUPLENAME;
    let partner_id = crate::pc::character_of(c, sd.id).map(|ch| ch.partner_id).unwrap_or(0);
    let name = if partner_id != 0 { crate::pc::charid2sd(c, partner_id).map(|p| p.name).unwrap_or_default() } else { String::new() };
    let p = PACKET_ZC_COUPLENAME { packetType: PACKET_ZC_COUPLENAME::ID as i16, name };
    crate::clif::send(c, &p.to_vec(), sd, crate::clif::Target::Area);
}

/// The family members WE_CALL* check (skill.cpp:4926-4952): `p_sd != nullptr && (p_sd->state.autotrade ||
/// p_sd->status.disable_call)`. sd->status.disable_call is not in this build's character data: 0, as for every
/// character created here.
fn family_refuses(c: &Ctx, char_id: u32) -> bool {
    char_id != 0 && crate::pc::charid2sd(c, char_id).map(|u| crate::autotrade::autotrade(c, u.id) != 0).unwrap_or(false)
}

/// skill.cpp skill_castend_id (Func::SkillCastendId; also called directly with tid 0 for an instant cast).
pub fn skill_castend_id(c: &Ctx, tid: u64, tick: Tick, id: u32, data: i64) -> i32 {
    let Some(mut src) = unit::get(c, id) else {
        log::debug!("skill_castend_id: src == nullptr (tid={tid}, id={id})");
        return 0; // not found
    };
    let is_pc = src.kind == BL_PC;
    let is_mob = src.kind == BL_MOB;
    if !src.on_map {
        src.ud.skilltimer = 0;
        unit::save(c, &src);
        return 0;
    }
    if src.ud.skill_id != SA_CASTCANCEL && src.ud.skill_id != SO_SPELLFIST {
        // otherwise handled in unit_skillcastcancel()
        if src.ud.skilltimer != tid {
            log::error!("skill_castend_id: Timer mismatch {}!={}!", src.ud.skilltimer, tid);
            src.ud.skilltimer = 0;
            unit::save(c, &src);
            return 0;
        }
        let freecast = is_pc && src.ud.skilltimer != 0 && (crate::skill::checkskill(c, id, SA_FREECAST) > 0 || src.ud.skill_id == LG_EXEEDBREAK);
        src.ud.skilltimer = 0;
        unit::save(c, &src);
        if freecast {
            // restore original walk speed
            super::unit::freecast_recalc(c, id);
        }
    }
    let src = unit::get(c, id).unwrap_or(src);
    let target = if src.ud.skilltarget == id { Some(src.clone()) } else { unit::get(c, src.ud.skilltarget) };
    // Use a do so that you can break out of it when the skill fails.
    let mut flag = 0;
    let ok = 'body: {
        let mut fail = false;
        let mut res = USESKILL_FAIL_LEVEL as i32;
        if sc::status_isdead(c, &src) {
            break 'body false;
        }
        // These actions happen even if the skill fails except when the caster is already dead
        let mut s = src.clone();
        crate::battle::unit_attack::unit_set_attackdelay(c, &mut s, tick, crate::battle::unit_attack::DELAY_EVENT_CASTEND);
        unit::save(c, &s);
        if is_mob {
            crate::mob::skill::castend_emotion(c, id);
        }
        let Some(target) = target.clone().filter(|t| t.on_map) else { break 'body false };
        let src = unit::get(c, id).unwrap_or(s);
        if src.map != target.map {
            break 'body false;
        }
        let skill_id = src.ud.skill_id;
        let skill_lv = src.ud.skill_lv;
        // These should become skill_castend_pos
        match skill_id {
            WE_CALLPARTNER => {
                if is_pc {
                    let partner = crate::pc::character_of(c, id).map(|ch| ch.partner_id).unwrap_or(0);
                    if family_refuses(c, partner) {
                        fail = true;
                    } else {
                        clif_callpartner(c, &src);
                    }
                }
            }
            WE_CALLPARENT => {
                if is_pc {
                    let ch = crate::pc::character_of(c, id);
                    let (f, m) = ch.map(|ch| (ch.father, ch.mother)).unwrap_or((0, 0));
                    if family_refuses(c, f) || family_refuses(c, m) {
                        fail = true;
                    }
                }
            }
            WE_CALLBABY => {
                if is_pc {
                    let child = crate::pc::character_of(c, id).map(|ch| ch.child).unwrap_or(0);
                    if family_refuses(c, child) {
                        fail = true;
                    }
                }
            }
            AM_RESURRECTHOMUN | PF_SPIDERWEB => {
                // Find a random spot to place the skill. [Skotlex]
                let splash = skill_get_splash(skill_id, skill_lv) as i16;
                let mut s = src.clone();
                s.ud.skillx = target.x + splash;
                s.ud.skilly = target.y + splash;
                if splash != 0 {
                    match map_random_dir(c, &target, s.ud.skillx, s.ud.skilly) {
                        Some((x, y)) => {
                            s.ud.skillx = x;
                            s.ud.skilly = y;
                        }
                        None => {
                            s.ud.skillx = target.x;
                            s.ud.skilly = target.y;
                        }
                    }
                }
                s.ud.skilltimer = tid;
                unit::save(c, &s);
                return skill_castend_pos(c, tid, tick, id, data);
            }
            GN_WALLOFTHORN | SC_ESCAPE | WL_FROSTMISTY | SU_CN_POWDERING | AG_RAIN_OF_CRYSTAL => {
                let mut s = src.clone();
                s.ud.skillx = target.x;
                s.ud.skilly = target.y;
                s.ud.skilltimer = tid;
                unit::save(c, &s);
                return skill_castend_pos(c, tid, tick, id, data);
            }
            _ => {}
        }
        // Failing
        if fail || {
            res = skill_castend_id_check(c, &src, &target, skill_id, skill_lv);
            res >= 0
        } {
            if is_pc && res != USESKILL_FAIL_MAX as i32 {
                clif_skill_fail(c, &src, skill_id, res as u8, 0, 0);
            }
            break 'body false;
        }
        // Avoid doing double checks for instant-cast skills.
        if tid != 0 && !crate::status::check::status_check_skilluse(c, Some(&src), Some(&target), skill_id, 1) {
            break 'body false;
        }
        if src.id != target.id && conf::skill_add_range() != 0 {
            let r = super::misc::skill_get_range2(c, &src, skill_id, skill_lv, true) + conf::skill_add_range();
            if !crate::path::check_distance((src.x - target.x) as i32, (src.y - target.y) as i32, r) {
                if is_pc {
                    skill_fail(c, &src, skill_id);
                    if conf::skill_out_range_consume() {
                        // Consume items anyway. [Skotlex]
                        super::condition::skill_consume_requirement(c, &src, skill_id, skill_lv, 3);
                    }
                }
                break 'body false;
            }
        }
        // OFFICIAL_WALKPATH
        if skill_get_casttype(skill_id) != CastType::NoDamage && !crate::path::path_search_long(src.map, src.x, src.y, target.x, target.y, crate::map::CellChk::ChkWall) {
            if is_pc {
                skill_fail(c, &src, skill_id);
                super::condition::skill_consume_requirement(c, &src, skill_id, skill_lv, 3); // Consume items anyway.
            }
            break 'body false;
        }
        if is_pc {
            if !super::condition::skill_check_condition_castend(c, &src, skill_id, skill_lv) {
                break 'body false;
            }
            let s = unit::get(c, id).unwrap_or_else(|| src.clone());
            super::condition::skill_consume_requirement(c, &s, skill_id, skill_lv, 1);
            let mut add_ap = skill_get_giveap(skill_id, skill_lv);
            // Give AP
            if add_ap > 0 {
                match skill_id {
                    // Don't know the official increase. For now lets do up to 50% increase. Retrospection does the same
                    // for song skills. [Rytech]
                    TR_ROSEBLOSSOM | TR_RHYTHMSHOOTING | TR_METALIC_FURY | TR_SOUNDBLEND => {
                        add_ap += add_ap * (10 * crate::skill::checkskill(c, id, TR_STAGE_MANNER) as i32) / 100;
                    }
                    TR_GEF_NOCTURN | TR_ROKI_CAPRICCIO | TR_AIN_RHAPSODY | TR_MUSICAL_INTERLUDE | TR_JAWAII_SERENADE | TR_NIPELHEIM_REQUIEM | TR_PRON_MARCH => {
                        if super::state::get(c, id).skill_id_old == TR_RETROSPECTION {
                            add_ap += add_ap * 50 / 100;
                            super::state::with(c, id, |s| s.skill_id_old = skill_id); // Prevents AP bonus on non Retro Spection use.
                        }
                    }
                    WH_CRESCIVE_BOLT => {
                        if sc::get_sce(c, id, SC_CRESCIVEBOLT).map(|e| e.val1 >= 3).unwrap_or(false) {
                            add_ap += 2;
                        }
                    }
                    SH_HYUN_ROK_CANNON => {
                        if crate::skill::checkskill(c, id, SH_COMMUNE_WITH_HYUN_ROK) > 0 || sc::has_sce(c, id, SC_TEMPORARY_COMMUNION) {
                            add_ap += 1;
                        }
                    }
                    _ => {}
                }
                crate::status::hp::status_heal(c, id, 0, 0, add_ap as i64, 0);
            }
            let is_emp = crate::battle::bl::class_of(c, &target) == MOBID_EMPERIUM;
            if src.id != target.id && (has_mode(c, &target, crate::status::defs::MD_SKILLIMMUNE) || (is_emp && !skill_get_inf2(skill_id, &[INF2_TARGETEMPERIUM]))) && skill_get_casttype(skill_id) == CastType::Damage {
                let s = unit::get(c, id).unwrap_or_else(|| src.clone());
                skill_fail(c, &s, skill_id);
                break 'body false; // Show a skill fail message (Damage type consumes requirements)
            }
        }
        if (src.kind == BL_MER || src.kind == BL_HOM) && !super::iface::skill_check_condition_mercenary(c, &src, skill_id, skill_lv, 1) {
            break 'body false;
        }
        let mut s = unit::get(c, id).unwrap_or_else(|| src.clone());
        if s.ud.running && skill_id == TK_JUMPKICK {
            s.ud.running = false;
            unit::save(c, &s);
            sc::status_change_end(c, id, SC_RUN, 0);
            flag = 1;
        }
        let mut s = unit::get(c, id).unwrap_or(s);
        if s.ud.walktimer != 0 && skill_id != TK_RUN && skill_id != RA_WUGDASH {
            unit::stop_walking(c, &mut s, USW_FIXPOS, 0);
        }
        let skillitem = if is_pc { super::state::get(c, id).skillitem } else { 0 };
        if !is_pc || skillitem != skill_id as i16 || skill_get_delay(skill_id, skill_lv) != 0 {
            let d = super::castfix::skill_delayfix(c, &s, skill_id, skill_lv) as i64;
            s.ud.canact_tick = (tick + d).max(s.ud.canact_tick - SECURITY_CASTTIME);
        }
        unit::save(c, &s);
        // Cooldown application
        match src.kind {
            BL_PC => {
                // Increases/Decreases cooldown of a skill by item/card bonuses.
                let cooldown = super::castfix::pc_get_skillcooldown(c, id, skill_id, skill_lv);
                if cooldown > 0 {
                    super::cooldown::skill_blockpc_start(c, id, skill_id, cooldown as i64);
                }
            }
            BL_HOM => {
                // (skill.cpp:5088-5096, RENEWAL) skill_blockhomun_start(hd, skill_id, skill_get_cooldown(â€¦))
                crate::homunculus::cooldown::skill_blockhomun_start(c, id, skill_id, skill_get_cooldown(skill_id, skill_lv) as i64);
            }
            BL_MER => {
                // skill_blockmerc_start(mc, ud->skill_id, skill_get_cooldown(ud->skill_id, ud->skill_lv)) (skill.cpp:5098-5103)
                crate::mercenary::skill::skill_blockmerc_start(c, id, skill_id, skill_get_cooldown(skill_id, skill_lv) as Tick);
            }
            _ => {}
        }
        if crate::conf::display_status_timers() && is_pc {
            let s = unit::get(c, id).unwrap_or_else(|| src.clone());
            let d = super::castfix::skill_delayfix(c, &s, skill_id, skill_lv);
            crate::status::clif::status_change(c, &s, EFST_POSTDELAY, 1, d as i64, 0, 0, 0);
        }
        if is_pc {
            match skill_id {
                GS_DESPERADO | RL_FIREDANCE => {
                    let t = skill_get_time(skill_id, skill_lv) as i64;
                    super::state::with(c, id, |s| s.canequip_tick = tick + t);
                }
                KN_BRANDISHSPEAR | CR_GRANDCROSS => {
                    let type_ = if skill_id == KN_BRANDISHSPEAR { SC_STRIPWEAPON } else { SC_STRIPSHIELD };
                    let dur = skill_get_time(skill_id, skill_lv) as i64;
                    let keep = sc::get_sce(c, id, type_)
                        .and_then(|e| (e.timer != 0).then(|| timer::due(c, e.timer)).flatten())
                        .map(|due| due - (c.tick() + dur) > 0)
                        .unwrap_or(false);
                    if !keep {
                        let s = unit::get(c, id).unwrap_or_else(|| src.clone());
                        sc::sc_start2(c, Some(&s), &s, type_, 100, 0, 1, dur);
                        // During Grand Cross you are in ankle state (cannot move or teleport)
                        if skill_id == CR_GRANDCROSS {
                            let s = unit::get(c, id).unwrap_or(s);
                            sc::sc_start(c, Some(&s), &s, SC_ANKLE, 100, 0, dur);
                        }
                    }
                }
                _ => {}
            }
        }
        if skill_get_state(skill_id) != ST_MOVE_ENABLE {
            // Monsters don't need a default walk delay as their AI is inactive after using a skill in general
            let wd = skill_get_walkdelay(skill_id, skill_lv) as i64;
            let d = if src.kind == BL_MOB { wd } else { conf::default_walk_delay() + wd };
            crate::battle::unit_attack::unit_set_walkdelay(c, id, tick, d, 1, 0);
        }
        if conf::skill_log() != 0 && conf::skill_log() & src.kind != 0 {
            log::info!("Type {}, ID {} skill castend id [id ={skill_id}, lv={skill_lv}, target ID {}]", src.kind, id, target.id);
        }
        let s = unit::get(c, id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        if skill_get_casttype(skill_id) == CastType::NoDamage {
            skill_castend_nodamage_id(c, &s, &t, skill_id, skill_lv, tick, flag);
        } else {
            skill_castend_damage_id(c, &s, &t, skill_id, skill_lv, tick, flag);
        }
        if is_pc {
            let st = super::state::get(c, id);
            let canact = unit::get(c, id).map(|u| u.ud.canact_tick).unwrap_or(tick);
            if st.keep_using_tid == 0 && st.keep_using_skill_id > 0 && st.keep_using_skill_id == skill_id {
                let s = unit::get(c, id).unwrap_or_else(|| src.clone());
                if !super::condition::skill_is_not_ok(c, &s, skill_id) && super::condition::skill_check_condition_castbegin(c, &s, skill_id, skill_lv) {
                    let t = timer::add(c, canact + 100, Func::SkillKeepUsing, id, 0);
                    super::state::with(c, id, |s| s.keep_using_tid = t);
                }
            }
        }
        if crate::battle::mobif::has_sc(src.kind) && !sc::load(c, id).empty() {
            if skill_id != RA_CAMOUFLAGE {
                sc::status_change_end(c, id, SC_CAMOUFLAGE, 0); // Applies to the first skill if active
            }
            if let Some(mut e) = sc::get_sce(c, id, SC_SPIRIT) {
                if e.val2 == SL_WIZARD as i32 && e.val3 == skill_id as i32 && skill_id != WZ_WATERBALL {
                    e.val3 = 0; // Clear bounced spell check.
                    sc::put_sce(c, &e);
                }
            }
        }
        if is_pc && skill_id != SA_ABRACADABRA {
            // they just set the data so leave it as it is.[Inkfish]
            super::state::with(c, id, |s| {
                s.skillitem = 0;
                s.skillitemlv = 0;
                s.skillitem_keep_requirement = false;
            });
        }
        if let Some(mut s) = unit::get(c, id) {
            if s.ud.skilltimer == 0 {
                if is_mob {
                    if let Some(mut md) = crate::mob::get(c, id) {
                        md.skill_idx = -1;
                        crate::mob::put(c, &md);
                    }
                } else {
                    s.ud.skill_id = 0; // mobs can't clear this one as it is used for skill condition 'afterskill'
                }
                s.ud.skill_lv = 0;
                s.ud.skilltarget = 0;
                unit::save(c, &s);
            }
        }
        true
    };
    if ok {
        return 1;
    }
    // Skill failed.
    let Some(mut s) = unit::get(c, id) else { return 0 };
    let sc_fogwall = sc::has_sce(c, id, SC_FOGWALL);
    if s.ud.skill_id == MO_EXTREMITYFIST && is_pc && !sc_fogwall {
        // When Asura fails... (except when it fails from Wall of Fog)
        // Consume SP/spheres
        let (skill_id, skill_lv) = (s.ud.skill_id, s.ud.skill_lv);
        super::condition::skill_consume_requirement(c, &s, skill_id, skill_lv, 1);
        crate::status::hp::status_set_sp(c, id, 0, 0);
        let s2 = unit::get(c, id).unwrap_or_else(|| s.clone());
        sc::sc_start(c, Some(&s2), &s2, SC_EXTREMITYFIST, 100, skill_lv as i32, skill_get_time(skill_id, skill_lv) as i64);
        sc::status_change_end(c, id, SC_EXPLOSIONSPIRITS, 0);
        sc::status_change_end(c, id, SC_BLADESTOP, 0);
        if let Some(target) = target.as_ref().filter(|t| t.map == s.map) {
            // Move character to target anyway.
            let s2 = unit::get(c, id).unwrap_or_else(|| s.clone());
            let dir = crate::map::calc_dir(&s2, target.x, target.y);
            // Move 3 cells (From Caster)
            let x: i16 = if dir > 0 && dir < 4 {
                -3
            } else if dir > 4 {
                3
            } else {
                0
            };
            let y: i16 = if dir > 2 && dir < 6 {
                -3
            } else if dir == 7 || dir < 2 {
                3
            } else {
                0
            };
            if unit::movepos(c, id, s2.x + x, s2.y + y, 1, true) {
                // Display movement + animation.
                if let Some(s3) = unit::get(c, id) {
                    crate::battle::clif::clif_blown(c, &s3);
                    super::clif::clif_spiritball(c, &s3, None, crate::clif::Target::Area);
                }
            }
            let s3 = unit::get(c, id).unwrap_or(s2);
            let amotion = crate::status::status_get_status_data(c, &s3).amotion as i32;
            if let Some(t) = unit::get(c, target.id) {
                clif_skill_damage(c, &s3, &t, tick, amotion, 0, 0, 1, skill_id, skill_lv as i32, DMG_SPLASH);
            }
        }
        s = unit::get(c, id).unwrap_or(s);
    }
    let skillitem = if is_pc { super::state::get(c, id).skillitem } else { 0 };
    s.ud.skill_id = 0;
    s.ud.skilltarget = 0;
    // (rAthena compares skillitem with the skill id it has just cleared: 0)
    if !is_pc || skillitem != 0 || skill_get_delay(0, s.ud.skill_lv) != 0 {
        s.ud.canact_tick = tick;
    }
    unit::save(c, &s);
    // You can't place a skill failed packet here because it would be sent in ALL cases, even cases where
    // skill_check_condition fails which would lead to double 'skill failed' messages u.u [Skotlex]
    if is_pc {
        let st = super::state::get(c, id);
        super::state::with(c, id, |x| {
            x.skillitem = 0;
            x.skillitemlv = 0;
            x.skillitem_keep_requirement = false;
            if x.keep_using_skill_id > 0 {
                x.keep_using_skill_id = 0;
                x.keep_using_tid = 0;
            }
        });
        if st.keep_using_skill_id > 0 && st.keep_using_tid != 0 {
            timer::delete(c, st.keep_using_tid);
        }
    } else if is_mob {
        if let Some(mut md) = crate::mob::get(c, id) {
            md.skill_idx = -1;
            crate::mob::put(c, &md);
        }
    }
    0
}

/// skill.cpp skill_castend_pos (Func::SkillCastendPos; also called directly with tid 0 for an instant cast).
pub fn skill_castend_pos(c: &Ctx, tid: u64, tick: Tick, id: u32, _data: i64) -> i32 {
    let Some(mut src) = unit::get(c, id) else { return 0 };
    let is_pc = src.kind == BL_PC;
    let is_mob = src.kind == BL_MOB;
    if !src.on_map {
        src.ud.skilltimer = 0;
        unit::save(c, &src);
        return 0;
    }
    if src.ud.skilltimer != tid {
        log::error!("skill_castend_pos: Timer mismatch {}!={}", src.ud.skilltimer, tid);
        src.ud.skilltimer = 0;
        unit::save(c, &src);
        return 0;
    }
    let freecast = is_pc && src.ud.skilltimer != 0 && (crate::skill::checkskill(c, id, SA_FREECAST) > 0 || src.ud.skill_id == LG_EXEEDBREAK);
    src.ud.skilltimer = 0;
    unit::save(c, &src);
    if freecast {
        // restore original walk speed
        super::unit::freecast_recalc(c, id);
    }
    let ok = 'body: {
        let src = unit::get(c, id).unwrap_or_else(|| src.clone());
        if sc::status_isdead(c, &src) {
            break 'body false;
        }
        // These actions happen even if the skill fails except when the caster is already dead
        let mut s = src.clone();
        crate::battle::unit_attack::unit_set_attackdelay(c, &mut s, tick, crate::battle::unit_attack::DELAY_EVENT_CASTEND);
        unit::save(c, &s);
        if is_mob {
            crate::mob::skill::castend_emotion(c, id);
        }
        let (skill_id, skill_lv, x, y) = (s.ud.skill_id, s.ud.skill_lv, s.ud.skillx, s.ud.skilly);
        if !super::iface::skill_pos_maxcount_check(c, &s, x, y, skill_id, skill_lv, s.kind, true) {
            break 'body false;
        }
        if tid != 0 {
            // Avoid double checks on instant cast skills. [Skotlex]
            if !crate::status::check::status_check_skilluse(c, Some(&s), None, skill_id, 1) {
                break 'body false;
            }
            if conf::skill_add_range() != 0 {
                let r = super::misc::skill_get_range2(c, &s, skill_id, skill_lv, true) + conf::skill_add_range();
                if !crate::path::check_distance((s.x - x) as i32, (s.y - y) as i32, r) {
                    if is_pc && conf::skill_out_range_consume() {
                        super::condition::skill_consume_requirement(c, &s, skill_id, skill_lv, 3); // Consume items anyway.
                    }
                    break 'body false;
                }
            }
        }
        if is_pc {
            if skill_id != AL_WARP && !super::condition::skill_check_condition_castend(c, &s, skill_id, skill_lv) {
                break 'body false;
            }
            let s2 = unit::get(c, id).unwrap_or_else(|| s.clone());
            super::condition::skill_consume_requirement(c, &s2, skill_id, skill_lv, 1);
            let mut add_ap = skill_get_giveap(skill_id, skill_lv);
            // Give AP
            if add_ap > 0 {
                if matches!(skill_id, WH_DEEPBLINDTRAP | WH_SOLIDTRAP | WH_SWIFTTRAP | WH_FLAMETRAP) && crate::skill::checkskill(c, id, WH_ADVANCED_TRAP) >= 3 {
                    add_ap += 1;
                }
                crate::status::hp::status_heal(c, id, 0, 0, add_ap as i64, 0);
            }
        }
        if (s.kind == BL_MER || s.kind == BL_HOM) && !super::iface::skill_check_condition_mercenary(c, &s, skill_id, skill_lv, 1) {
            break 'body false;
        }
        if conf::skill_log() != 0 && conf::skill_log() & s.kind != 0 {
            log::info!("Type {}, ID {} skill castend pos [id ={skill_id}, lv={skill_lv}, ({x},{y})]", s.kind, id);
        }
        let mut s = unit::get(c, id).unwrap_or(s);
        if s.ud.walktimer != 0 {
            unit::stop_walking(c, &mut s, USW_FIXPOS, 0);
        }
        let skillitem = if is_pc { super::state::get(c, id).skillitem } else { 0 };
        if !is_pc || skillitem != skill_id as i16 || skill_get_delay(skill_id, skill_lv) != 0 {
            let d = super::castfix::skill_delayfix(c, &s, skill_id, skill_lv) as i64;
            s.ud.canact_tick = (tick + d).max(s.ud.canact_tick - SECURITY_CASTTIME);
        }
        unit::save(c, &s);
        if is_pc {
            // Cooldown application
            let cooldown = super::castfix::pc_get_skillcooldown(c, id, skill_id, skill_lv);
            if cooldown != 0 {
                super::cooldown::skill_blockpc_start(c, id, skill_id, cooldown as i64);
            }
        }
        if crate::conf::display_status_timers() && is_pc {
            let d = super::castfix::skill_delayfix(c, &s, skill_id, skill_lv);
            crate::status::clif::status_change(c, &s, EFST_POSTDELAY, 1, d as i64, 0, 0, 0);
        }
        // Monsters don't need a default walk delay as their AI is inactive after using a skill in general
        let wd = skill_get_walkdelay(skill_id, skill_lv) as i64;
        let d = if s.kind == BL_MOB { wd } else { conf::default_walk_delay() + wd };
        crate::battle::unit_attack::unit_set_walkdelay(c, id, tick, d, 1, 0);
        let s = unit::get(c, id).unwrap_or(s);
        skill_castend_pos2(c, &s, x, y, skill_id, skill_lv, tick, 0);
        if skill_id != RA_CAMOUFLAGE {
            sc::status_change_end(c, id, SC_CAMOUFLAGE, 0); // Applies to the first skill if active
        }
        if is_pc && super::state::get(c, id).skillitem != AL_WARP as i16 {
            // Warp-Portal thru items will clear data in skill_castend_map. [Inkfish]
            super::state::with(c, id, |x| {
                x.skillitem = 0;
                x.skillitemlv = 0;
                x.skillitem_keep_requirement = false;
            });
        }
        if let Some(mut s) = unit::get(c, id) {
            if s.ud.skilltimer == 0 {
                if is_mob {
                    if let Some(mut md) = crate::mob::get(c, id) {
                        md.skill_idx = -1;
                        crate::mob::put(c, &md);
                    }
                } else {
                    s.ud.skill_id = 0; // Non mobs can't clear this one as it is used for skill condition 'afterskill'
                }
                s.ud.skill_lv = 0;
                s.ud.skillx = 0;
                s.ud.skilly = 0;
                unit::save(c, &s);
            }
        }
        true
    };
    if ok {
        return 1;
    }
    let Some(mut s) = unit::get(c, id) else { return 0 };
    let skillitem = if is_pc { super::state::get(c, id).skillitem } else { 0 };
    if !is_pc || skillitem != s.ud.skill_id as i16 || skill_get_delay(s.ud.skill_id, s.ud.skill_lv) != 0 {
        s.ud.canact_tick = tick;
    }
    s.ud.skill_id = 0;
    s.ud.skill_lv = 0;
    unit::save(c, &s);
    if is_pc {
        super::state::with(c, id, |x| {
            x.skillitem = 0;
            x.skillitemlv = 0;
            x.skillitem_keep_requirement = false;
        });
    } else if is_mob {
        if let Some(mut md) = crate::mob::get(c, id) {
            md.skill_idx = -1;
            crate::mob::put(c, &md);
        }
    }
    0
}

/// skill.cpp skill_castend_pos2 (the skillunit agent's shell, skill/iface.rs until its merge).
#[allow(clippy::too_many_arguments)]
pub fn skill_castend_pos2(c: &Ctx, src: &Unit, x: i16, y: i16, skill_id: u16, skill_lv: u16, tick: Tick, flag: i32) -> i32 {
    super::iface::skill_castend_pos2(c, src, x, y, skill_id, skill_lv, tick, flag)
}

/// The requirement consumption / last-cast bookkeeping at the end of skill_castend_damage_id / nodamage_id:
/// `sd->canskill_tick`, the arrow of a ranged skill (battle_consume_ammo), skill_onskillusage (nodamage only),
/// skill_consume_requirement(…, 2).
fn castend_tail(c: &Ctx, src: &Unit, bl: &Unit, skill_id: u16, skill_lv: u16, tick: Tick, flag: i32, onskillusage: bool) {
    if src.kind != BL_PC || flag & 1 != 0 {
        return;
    }
    // ensure that the skill last-cast tick is recorded
    super::state::with(c, src.id, |s| s.canskill_tick = c.tick());
    if crate::battle::state::get(c, src.id).arrow_atk {
        // consume arrow on last invocation to this skill.
        crate::battle::weapon_attack::battle_consume_ammo(c, src.id, skill_id, skill_lv);
    }
    if onskillusage {
        let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
        super::effect::skill_onskillusage(c, src.id, Some(&t), skill_id, tick);
    }
    // perform skill requirement consumption
    if flag & SKILL_NOCONSUME_REQ == 0 {
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        super::condition::skill_consume_requirement(c, &s, skill_id, skill_lv, 2);
    }
}

/// skill.cpp skill_castend_damage_id: an offensive skill hits `bl` (flag &1: a recursive call for a splash target).
pub fn skill_castend_damage_id(c: &Ctx, src: &Unit, bl: &Unit, skill_id: u16, skill_lv: u16, tick: Tick, flag: i32) -> i32 {
    if skill_id > 0 && skill_lv == 0 {
        return 0;
    }
    let mut flag = flag;
    if src.map != bl.map {
        return 1;
    }
    if !bl.on_map {
        return 1;
    }
    let is_pc = src.kind == BL_PC;
    if sc::status_isdead(c, bl) {
        return 1;
    }
    if skill_id != 0 && skill_id != AG_DEADLY_PROJECTION && skill_get_type(skill_id) == crate::status::defs::BF_MAGIC && sc::status_isimmune(c, bl) == 100 {
        // GTB makes all targetted magic display miss with a single bolt.
        let sct = skill_get_sc(skill_id) as i32;
        if sct != SC_NONE {
            sc::status_change_end(c, bl.id, sct, 0);
        }
        let amotion = crate::status::status_get_status_data(c, src).amotion as i32;
        let dmotion = crate::status::status_get_status_data(c, bl).dmotion as i32;
        let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
        clif_skill_damage(c, src, &t, tick, amotion, dmotion, 0, 1, skill_id, skill_lv as i32, skill_get_hit(skill_id));
        return 1;
    }
    let tstatus = crate::status::status_get_status_data(c, bl);
    match skill_id {
        0 => {
            // no skill - basic/normal attack
            if is_pc {
                if flag & 3 != 0 {
                    if bl.id as i32 != area::temp(1) {
                        super::attack::skill_attack(c, crate::status::defs::BF_WEAPON, src, src, bl, skill_id, skill_lv, tick, SD_LEVEL | flag);
                    }
                } else {
                    area::set_temp(1, bl.id as i32);
                    let splash_range = crate::status::data::sd_get(c, src.id).map(|s| s.bonus.splash_range).unwrap_or(0) as i16;
                    let f = flag | BCT_ENEMY | 1;
                    foreach_in_range(c, bl, splash_range, BL_CHAR, Wall::None, |c, t| area::skill_area_sub(c, t, src, skill_id, skill_lv, tick, f, skill_castend_damage_id));
                    flag |= 1; // Set flag to 1 so ammo is not double-consumed. [Skotlex]
                }
            }
        }
        _ => match crate::skills::skill_impl(skill_id) {
            Some(imp) => {
                let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
                imp.castend_damage_id(c, &s, &t, skill_lv, tick, &mut flag);
            }
            None => {
                log::warn!("skill_castend_damage_id: Unknown skill used:{skill_id}{}", no_impl_note(skill_id));
                let amotion = crate::status::status_get_status_data(c, src).amotion as i32;
                let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
                clif_skill_damage(c, src, &t, tick, amotion, tstatus.dmotion as i32, 0, skill_get_num(skill_id, skill_lv).abs() as i16, skill_id, skill_lv as i32, skill_get_hit(skill_id));
                return 1;
            }
        },
    }
    if crate::battle::mobif::has_sc(src.kind) && sc::has_sce(c, src.id, SC_CURSEDCIRCLE_ATKER) {
        // Should only remove after the skill has been casted.
        sc::status_change_end(c, src.id, SC_CURSEDCIRCLE_ATKER, 0);
    }
    castend_tail(c, src, bl, skill_id, skill_lv, tick, flag, false);
    0
}

/// The family the pipeline's "no impl" warning names (the next wave's agents port those classes).
fn no_impl_note(skill_id: u16) -> String {
    if skill_db().exists(skill_id) {
        " (its class is not ported yet: the next wave's skill families)".to_string()
    } else {
        String::new()
    }
}

/// skill.cpp skill_castend_nodamage_id: a supportive / status skill takes effect on `bl` (flag &1: recursive).
pub fn skill_castend_nodamage_id(c: &Ctx, src: &Unit, bl: &Unit, skill_id: u16, skill_lv: u16, tick: Tick, flag: i32) -> i32 {
    if skill_id > 0 && skill_lv == 0 {
        return 0; // celest
    }
    let mut flag = flag;
    if src.map != bl.map {
        return 1;
    }
    let is_pc = src.kind == BL_PC;
    if !bl.on_map {
        return 1;
    }
    if sc::status_isdead(c, src) {
        return 1;
    }
    if src.id != bl.id && sc::status_isdead(c, bl) {
        // Skills that may be cast on dead targets
        if !matches!(skill_id, NPC_WIDESOULDRAIN | PR_REDEMPTIO | ALL_RESURRECTION | WM_DEADHILLHERE | WE_ONEFOREVER) {
            return 1;
        }
    }
    let tstatus = crate::status::status_get_status_data(c, bl);
    // Check for undead skills that convert a no-damage skill into a damage one. [Skotlex]
    match skill_id {
        AL_HEAL | ALL_RESURRECTION | PR_ASPERSIO | AB_HIGHNESSHEAL => {
            // Apparently only player casted skills can be offensive like this.
            if is_pc && sc::battle_check_undead(tstatus.race as i32, tstatus.def_ele as i32) {
                if crate::battle::target::battle_check_target(c, src, bl, BCT_ENEMY) < 1 {
                    // Offensive heal does not works on non-enemies. [Skotlex]
                    skill_fail(c, src, skill_id);
                    return 0;
                }
                return skill_castend_damage_id(c, src, bl, skill_id, skill_lv, tick, flag);
            }
        }
        NPC_SMOKING => {
            // Since it is a self skill, this one ends here rather than in damage_id. [Skotlex]
            return skill_castend_damage_id(c, src, bl, skill_id, skill_lv, tick, flag);
        }
        MH_STEINWAND => {
            // (skill_check_unit_range / skill_castend_pos2: skillunit's port; the homunculus: homunculus/)
            let master = crate::battle::target::battle_get_master(c, src);
            let mut ret = 0;
            if !super::iface::skill_check_unit_range(c, src, src.x, src.y, skill_id, skill_lv) {
                ret = skill_castend_pos2(c, src, src.x, src.y, skill_id, skill_lv, tick, flag); // cast on homon
            }
            // (s_src is never null: battle_get_master answers the caster itself when it has no master, which then
            // gets the "cast on master" check a second time, as in rAthena)
            if !super::iface::skill_check_unit_range(c, &master, master.x, master.y, skill_id, skill_lv) {
                ret |= skill_castend_pos2(c, &master, master.x, master.y, skill_id, skill_lv, tick, flag); // cast on master
            }
            return ret;
        }
        _ => {
            // Skill is actually ground placed.
            if src.id == bl.id && skill_get_unit_id(skill_id) != 0 {
                return skill_castend_pos2(c, src, bl.x, bl.y, skill_id, skill_lv, tick, 0);
            }
        }
    }
    let type_ = skill_get_sc(skill_id) as i32;
    // Skills that cause an status should be blocked if the target element blocks its element.
    if src.id != bl.id && type_ > SC_NONE {
        let i = skill_get_ele(skill_id, skill_lv);
        if crate::battle::fix::chk_element(i) && i > crate::status::defs::ELE_NEUTRAL && skill_get_inf(skill_id) != INF_SUPPORT_SKILL as i32 && crate::battle::fix::battle_attr_fix(c, None, None, 100, i, tstatus.def_ele as i32, tstatus.ele_lv as i32, 0) <= 0 {
            return 1;
        }
    }
    match skill_id {
        // New guild skills [Celest]
        GD_BATTLEORDER | GD_REGENERATION | GD_RESTORE | GD_EMERGENCY_MOVE => {
            let gid = |u: &Unit| crate::battle::target::status_get_guild_id(c, u);
            if flag & 1 != 0 {
                if gid(src) == gid(bl) {
                    if skill_id == GD_RESTORE {
                        let healed = crate::status::hp::status_percent_heal(c, bl.id, 90, 90, 0);
                        let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
                        clif_skill_nodamage(c, Some(src), &t, AL_HEAL, healed, true);
                    } else {
                        let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
                        sc::sc_start(c, Some(src), &t, type_, 100, skill_lv as i32, skill_get_time(skill_id, skill_lv) as i64);
                    }
                }
            } else if gid(src) != 0 {
                clif_skill_nodamage(c, Some(src), bl, skill_id, skill_lv as i32, true);
                let f = flag | BCT_GUILD | 1;
                foreach_in_range(c, src, skill_get_splash(skill_id, skill_lv) as i16, BL_PC, Wall::None, |c, t| {
                    area::skill_area_sub(c, t, src, skill_id, skill_lv, tick, f, skill_castend_nodamage_id)
                });
                if is_pc {
                    // RENEWAL: skill_blockpc_start(sd, skill_id, skill_get_cooldown)
                    super::cooldown::skill_blockpc_start(c, src.id, skill_id, skill_get_cooldown(skill_id, skill_lv) as i64);
                }
            }
        }
        GD_EMERGENCYCALL | GD_ITEMEMERGENCYCALL => guild_emergency_call(c, src, bl, skill_id, skill_lv),
        GD_CHARGESHOUT_FLAG => {
            let gx = crate::guild::gx(c, src.id);
            if is_pc && gx.gmaster_flag {
                if let Some(mut g) = crate::guild::sd_guild(c, src.id) {
                    let spawned = crate::mob::spawn::mob_once_spawn_sub(c, Some(src), src.map, src.x, src.y, Some(&g.guild.name), MOBID_GUILD_SKILL_FLAG, None, crate::status::defs::SZ_SMALL as u8, crate::mob::AI_GUILD);
                    if let Some(mut md) = spawned {
                        g.chargeshout_flag_id = md.id as i32;
                        crate::guild::put_guild(c, &g);
                        md.master_id = src.id;
                        if md.deletetimer != 0 {
                            timer::delete(c, md.deletetimer);
                        }
                        md.deletetimer = timer::add(c, c.tick() + skill_get_time(GD_CHARGESHOUT_FLAG, skill_lv) as i64, Func::MobTimerDelete, md.id, 0);
                        crate::mob::put(c, &md);
                        crate::mob::spawn::mob_spawn(c, md.id);
                    }
                }
            }
        }
        GD_CHARGESHOUT_BEATING => {
            let flag_unit = if is_pc { crate::guild::sd_guild(c, src.id).and_then(|g| unit::get(c, g.chargeshout_flag_id as u32).filter(|u| u.on_map)) } else { None };
            match flag_unit {
                Some(mob_bl) => {
                    if crate::pc::setpos(c, src.id, mob_bl.map, mob_bl.x, mob_bl.y, unit::CLR_RESPAWN) != crate::pc::SetPos::Ok {
                        skill_fail(c, src, skill_id);
                    } else if let Some(t) = unit::get(c, bl.id) {
                        clif_skill_nodamage(c, Some(src), &t, skill_id, skill_lv as i32, true);
                    }
                }
                None => {
                    if is_pc {
                        skill_fail(c, src, skill_id);
                    }
                }
            }
        }
        NPC_WIDEHEALTHFEAR | NPC_WIDEBODYBURNNING | NPC_WIDEFROSTMISTY | NPC_WIDECOLD | NPC_WIDE_DEEP_SLEEP | NPC_WIDESIREN => {
            // TODO (rAthena): skills undefined in skill_db.yml
            if flag & 1 != 0 {
                let t2 = skill_get_time2(skill_id, skill_lv) as i64;
                match type_ {
                    SC_BURNING => {
                        sc::sc_start4(c, Some(src), bl, type_, 100, skill_lv as i32, 1000, src.id as i32, 0, t2);
                    }
                    SC_STONEWAIT => {
                        // sc_start2(src, bl, type, 100, skill_lv, src->id, skill_get_time2(…), skill_get_time(…)):
                        // the last argument is sc_start2's delay
                        let delay = skill_get_time(skill_id, skill_lv);
                        sc::status_change_start(c, Some(src), bl, type_, 100 * 100, skill_lv as i32, src.id as i32, 0, 0, t2, crate::status::defs::SCSTART_NONE, delay);
                    }
                    _ => {
                        sc::sc_start2(c, Some(src), bl, type_, 100, skill_lv as i32, src.id as i32, t2);
                    }
                }
            } else {
                area::set_temp(2, 0); // For SD_PREAMBLE
                clif_skill_nodamage(c, Some(src), bl, skill_id, skill_lv as i32, true);
                let f = flag | BCT_ENEMY | SD_PREAMBLE | 1;
                foreach_in_range(c, bl, skill_get_splash(skill_id, skill_lv) as i16, BL_CHAR, Wall::None, |c, t| {
                    area::skill_area_sub(c, t, src, skill_id, skill_lv, tick, f, skill_castend_nodamage_id)
                });
            }
        }
        _ => match crate::skills::skill_impl(skill_id) {
            Some(imp) => {
                let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
                imp.castend_nodamage_id(c, &s, &t, skill_lv, tick, &mut flag);
            }
            None => {
                let name = skill_db().find(skill_id).map(|s| s.name.clone()).unwrap_or_else(|| "UNKNOWN".to_string());
                log::warn!("skill_castend_nodamage_id: missing code case for skill {name}({skill_id}){}", no_impl_note(skill_id));
                clif_skill_nodamage(c, Some(src), bl, skill_id, skill_lv as i32, true);
                return 1;
            }
        },
    }
    if skill_id != SR_CURSEDCIRCLE && skill_id != NPC_SR_CURSEDCIRCLE && crate::battle::mobif::has_sc(src.kind) && sc::has_sce(c, src.id, SC_CURSEDCIRCLE_ATKER) {
        // Should only remove after the skill had been casted.
        sc::status_change_end(c, src.id, SC_CURSEDCIRCLE_ATKER, 0);
    }
    if bl.kind == BL_MOB {
        // Mob skill event for no damage skills (damage ones are handled in battle_damage/skill_attack) [Skotlex]
        if conf::exp_bonus_nodamage_attacker() != 0 {
            crate::mob::dead::mob_log_damage(c, bl.id, src, 0, 0); // Log interaction (counts as 'attacker' for the exp bonus)
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        crate::mob::skill::mobskill_event(c, bl.id, &s, tick, MSC_SKILLUSED | ((skill_id as i32) << 16), 0);
    }
    castend_tail(c, src, bl, skill_id, skill_lv, tick, flag, true);
    0
}

/// skill_castend_nodamage_id's GD_EMERGENCYCALL / GD_ITEMEMERGENCYCALL arm: the guild members come to the caster.
fn guild_emergency_call(c: &Ctx, src: &Unit, bl: &Unit, skill_id: u16, skill_lv: u16) {
    let mut dx: [i16; 9] = [-1, 1, 0, 0, -1, 1, -1, 1, 0];
    let mut dy: [i16; 9] = [0, 0, 1, -1, 1, -1, -1, 1, 0];
    let is_pc = src.kind == BL_PC;
    // i don't know if it actually summons in a circle, but oh well. ;P
    let g = if is_pc { crate::guild::sd_guild(c, src.id) } else { crate::guild::guild_search(c, crate::battle::target::status_get_guild_id(c, src)) };
    let Some(g) = g else { return };
    let calls: u8 = if skill_id == GD_ITEMEMERGENCYCALL {
        match skill_lv {
            1 => 7,
            2 => 12,
            3 => 20,
            _ => 0,
        }
    } else {
        0
    };
    clif_skill_nodamage(c, Some(src), bl, skill_id, skill_lv as i32, true);
    let mut called: u8 = 0;
    let mut j: usize = 0;
    let max_member = g.guild.max_member.max(0) as usize;
    let mut i = 0;
    while i < max_member && (calls == 0 || called < calls) {
        if j > 8 {
            j = 0;
        }
        let dst = g.guild.member.get(i).map(|m| m.sd).unwrap_or(0);
        if dst != 0 && !(is_pc && dst == src.id) {
            if let Some(dstu) = unit::get(c, dst).filter(|u| crate::autotrade::autotrade(c, u.id) == 0 && !sc::status_isdead(c, u)) {
                // (dstsd->status.disable_call: 0)
                let skip = (crate::npc::mapflag(c, dstu.map, crate::npc::MF_NOWARP) && !crate::npc::mapflag_gvg2(c, dstu.map))
                    || !crate::pc::job_can_entermap(c, crate::pc::character_of(c, dst).map(|ch| ch.class as i32).unwrap_or(0), src.map, crate::pc::group_level(c, dst));
                if !skip {
                    if crate::map::getcell(c, src.map, src.x + dx[j], src.y + dy[j], crate::map::CellChk::ChkNoReach) {
                        dx[j] = 0;
                        dy[j] = 0;
                    }
                    if crate::pc::setpos(c, dst, src.map, src.x + dx[j], src.y + dy[j], unit::CLR_RESPAWN) == crate::pc::SetPos::Ok {
                        // (rAthena counts `!pc_setpos(…)`: SETPOS_OK is 0)
                        called += 1;
                    }
                }
            }
        }
        i += 1;
        j += 1;
    }
    if is_pc {
        // RENEWAL: skill_blockpc_start(sd, skill_id, skill_get_cooldown)
        super::cooldown::skill_blockpc_start(c, src.id, skill_id, skill_get_cooldown(skill_id, skill_lv) as i64);
    }
}
