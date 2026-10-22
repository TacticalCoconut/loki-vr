//! The NPC parts of unit.cpp / status.cpp / npc.cpp / clif.cpp that the script commands need (script2):
//! - unit_refresh, npc_setclass, clif_changelook's NPC branch;
//! - status_calc_npc (status_calc_bl_ with SCB_ALL for a BL_NPC: status_calc_npc_, status_calc_misc and the BL_NPC path
//!   of status_calc_bl_main) — an NPC's status lives in its `npc_misc` row (nd->status, nd->level, nd->params);
//! - walking NPCs (npcwalkto, unitwalk): the BL_NPC parts of unit_walktoxy_sub / unit_walktoxy_timer (a player-sprite NPC
//!   is re-spawned as a walking player unit and back, OnTouch areas follow the NPC, walk_done_event).


use crate::map::{self, BL_NPC, BL_PC};
use crate::npc::{npc, Npc, NPCTYPE_SCRIPT, NPCTYPE_WARP};
use crate::pc_misc::{npcm, put_npcm};
use crate::status::calc_sc::{self as cs, Who};
use crate::status::data::StatusData;
use crate::status::defs::*;
use crate::unit::{self, Unit};
use crate::world::Ctx;

// ---------------------------------------------------------------------------------------------- display

/// unit.cpp unit_refresh: fade the unit out (CLR_TRICKDEAD) and in again for the players of its map.
pub fn unit_refresh(c: &Ctx, u: &Unit, walking: bool) {
    if !u.on_map {
        return; // bl->m < 0
    }
    // mapdata->users
    if !map::players_on_map(c, u.map).is_empty() {
        crate::clif::clearunit_area(c, u, unit::CLR_TRICKDEAD); // Fade out
        crate::clif::spawn(c, u, walking); // Fade in
    }
}

/// npc.cpp npc_get_viewdata / status_set_viewdata (NPC): is there view data for this class? (npc view db ids, the
/// warp sprite, monsters and player classes; anything else keeps the old view data and logs an error)
pub fn npc_class_has_view(class: i32) -> bool {
    class == crate::npc::JT_FAKENPC || npcdb_checkid(class) || crate::data::mobdb_checkid(class) || crate::clif::pcdb_checkid(class)
}

/// npc.hpp npcdb_checkid: the NPC sprite ranges (NPC_RANGE1 44-126, NPC_RANGE2 400-1000, NPC_RANGE3 10000-20000,
/// exclusive) and JT_HIDDEN_WARP_NPC / JT_INVISIBLE.
pub fn npcdb_checkid(id: i32) -> bool {
    (id > 44 && id < 126) || id == crate::npc::JT_HIDDEN_WARP_NPC || (id > 400 && id < 1000) || id == crate::npc::JT_INVISIBLE || (id > 10000 && id < 20000)
}

/// npc.cpp npc_setclass: a new class (status_set_viewdata) and unit_refresh.
pub fn npc_setclass(c: &Ctx, nd_id: u32, class: i32) {
    let Some(mut nd) = c.db().npc().id().find(nd_id) else { return };
    if nd.class == class {
        return;
    }
    nd.class = class;
    c.db().npc().id().update(nd.clone());
    let Some(mut u) = unit::get(c, nd_id) else { return };
    if npc_class_has_view(class) {
        u.vd = crate::npc::npc_viewdata(class);
    } else {
        log::error!("status_set_viewdata (NPC): Invalid view data {class}");
    }
    unit::save(c, &u);
    unit_refresh(c, &u, false);
}

/// clif.cpp clif_changelook for an NPC: the look is written to its view data, then unit_refresh (PACKETVER >= 4 sends
/// no sprite change for NPCs). LOOK_BASE / LOOK_SHOES / LOOK_BODY write nothing (LOOK_BODY and JT_INVISIBLE return
/// before the refresh).
pub fn changelook_npc(c: &Ctx, id: u32, look: u8, val: i32) {
    use crate::pc_equip::*;
    let Some(mut u) = unit::get(c, id) else { return };
    match look {
        LOOK_WEAPON => u.vd.weapon = val,
        LOOK_SHIELD => u.vd.shield = val,
        LOOK_BASE => {
            if val == crate::npc::JT_INVISIBLE {
                return;
            }
            // the clothes colour / body of wedding, xmas, summer, hanbok, oktoberfest and costume options: status
            // changes on NPCs are never started here, so their option bits are the NPC's own (hide / cloak)
        }
        LOOK_HAIR => u.vd.hair = val,
        LOOK_HEAD_BOTTOM => u.vd.head_bottom = val,
        LOOK_HEAD_TOP => u.vd.head_top = val,
        LOOK_HEAD_MID => u.vd.head_mid = val,
        LOOK_HAIR_COLOR => u.vd.hair_color = val,
        LOOK_CLOTHES_COLOR => u.vd.clothes_color = val,
        LOOK_SHOES => {}
        LOOK_BODY => return, // unknown purpose, dont send something
        LOOK_ROBE => u.vd.robe = val,
        LOOK_BODY2 => u.vd.body2 = val,
        _ => {}
    }
    unit::save(c, &u);
    unit_refresh(c, &u, false);
}

// ---------------------------------------------------------------------------------------------- NPC status

fn who<'a>(c: &'a Ctx<'a>, id: u32, m: u16) -> Who<'a> {
    Who { c, kind: BL_NPC, id, map: m, pc: None }
}

fn cap(v: i32, lo: i32, hi: i32) -> i32 {
    v.clamp(lo, hi)
}

/// map.hpp e_element ELE_NEUTRAL, e_race RC_DEMIHUMAN.
const ELE_NEUTRAL: u8 = 0;
const RC_DEMIHUMAN: u8 = 7;

/// status.cpp status_base_atk (RENEWAL) for an NPC: enable_baseatk_renewal includes BL_NPC; "Others": str + level.
fn status_base_atk_npc(status: &StatusData, level: i32) -> i32 {
    if crate::script::battle_flags::battle_get_value("enable_baseatk_renewal") & BL_NPC as i32 == 0 {
        return 0;
    }
    let dstr = status.str as i32;
    cap(dstr + level, 0, u16::MAX as i32)
}

/// status.cpp status_base_matk_min / _max (RENEWAL, default branch — NPCs use the player formula).
fn status_base_matk_npc(status: &StatusData, level: i32) -> i32 {
    let (i, d, l, s) = (status.int_ as i32, status.dex as i32, status.luk as i32, status.spl as i32);
    (i + i / 2 + d / 5 + l / 3 + level / 4 + 5 * s) as u16 as i32
}

/// status.cpp status_calc_misc for a BL_NPC (RENEWAL).
pub fn status_calc_misc(status: &mut StatusData, level: i32) {
    // Non players get the value set
    status.batk = 0;
    status.hit = 0;
    status.flee = 0;
    status.def2 = 0;
    status.mdef2 = 0;
    status.cri = 0;
    status.flee2 = 0;
    status.patk = 0;
    status.smatk = 0;
    status.hplus = 0;
    status.crate_ = 0;
    status.res = 0; // (bl->type != BL_MOB)
    status.mres = 0;
    let s = |v: i16| v as i32;
    // Hit
    let mut stat = s(status.hit) + level + s(status.dex) + 150 + 2 * s(status.con);
    status.hit = cap(stat, 1, i16::MAX as i32) as i16;
    // Flee
    stat = s(status.flee) + level + s(status.agi) + 100 + 2 * s(status.con);
    status.flee = cap(stat, 1, i16::MAX as i32) as i16;
    // Def2
    stat = s(status.def2) + ((level as f32 + status.vit as f32) / 2.0) as i32;
    status.def2 = cap(stat, 0, i16::MAX as i32) as i16;
    // Mdef2
    stat = s(status.mdef2) + ((status.int_ as f32 + level as f32) / 4.0) as i32;
    status.mdef2 = cap(stat, 0, i16::MAX as i32) as i16;
    // PAtk / SMatk / Res / Mres / HPlus / CRate
    stat = s(status.patk) + s(status.pow) / 3 + s(status.con) / 5;
    status.patk = cap(stat, 0, i16::MAX as i32) as i16;
    stat = s(status.smatk) + s(status.spl) / 3 + s(status.con) / 5;
    status.smatk = cap(stat, 0, i16::MAX as i32) as i16;
    stat = s(status.res) + s(status.sta) + s(status.sta) / 3 * 5;
    status.res = cap(stat, 0, i16::MAX as i32) as i16;
    stat = s(status.mres) + s(status.wis) + s(status.wis) / 3 * 5;
    status.mres = cap(stat, 0, i16::MAX as i32) as i16;
    stat = s(status.hplus) + s(status.crt);
    status.hplus = cap(stat, 0, i16::MAX as i32) as i16;
    stat = s(status.crate_) + s(status.crt) / 3;
    status.crate_ = cap(stat, 0, i16::MAX as i32) as i16;
    // ATK (status_base_atk_max / _min, default branch)
    // status->rhw.atk2 = rhw.atk2; status->rhw.atk = rhw.atk
    // MAtk
    let m = status_base_matk_npc(status, level) as u16;
    status.matk_min = m;
    status.matk_max = m;
    // Critical: enable_critical does not include BL_NPC
    let bf = crate::script::battle_flags::battle_get_value;
    if bf("enable_critical") & BL_NPC as i32 != 0 {
        stat = s(status.cri) + level / 10 + 10 + s(status.luk) * 3;
        status.cri = cap(stat, 1, i16::MAX as i32) as i16;
    } else {
        status.cri = 0;
    }
    if bf("enable_perfect_flee") & BL_NPC as i32 != 0 {
        stat = s(status.flee2) + s(status.luk) + 10;
        status.flee2 = cap(stat, 0, i16::MAX as i32) as i16;
    } else {
        status.flee2 = 0;
    }
    status.batk += status_base_atk_npc(status, level);
    if status.cri != 0 {
        // default (non-player, non-monster): critical_rate
        let rate = bf("critical_rate");
        if rate != 100 {
            status.cri = cap(status.cri as i32 * rate / 100, 1, i16::MAX as i32) as i16;
        }
        if status.cri == 0 && rate != 0 {
            status.cri = 10;
        }
    }
    // (BL_REGEN does not include BL_NPC: no status_calc_regen)
}

/// status.cpp status_calc_npc_.
fn status_calc_npc_base(nd_id: u32, size: u8, speed: u16, m: &mut crate::pc_misc::NpcMisc, opt: u8) {
    let _ = nd_id;
    let status = &mut m.status;
    if opt & SCO_FIRST != 0 {
        status.hp = 1;
        status.sp = 1;
        status.max_hp = 1;
        status.max_sp = 1;
        status.def_ele = ELE_NEUTRAL;
        status.ele_lv = 1;
        status.race = RC_DEMIHUMAN;
        status.class_ = CLASS_NORMAL as u8;
        status.size = size;
        status.rhw.range = 1 + status.size as u16;
        status.mode = MD_CANMOVE | MD_CANATTACK;
        status.speed = speed;
    }
    let p = |i: usize| m.params.get(i).copied().unwrap_or(0) as i32;
    let sp = m.stat_point as i32;
    status.str = (sp + p(0)) as i16;
    status.agi = (sp + p(1)) as i16;
    status.vit = (sp + p(2)) as i16;
    status.int_ = (sp + p(3)) as i16;
    status.dex = (sp + p(4)) as i16;
    status.luk = (sp + p(5)) as i16;
    status_calc_misc(status, m.level as i32);
    // status_cpy(&nd->status, status): the same object
}

/// status.hpp status_calc_npc(nd, opt) = status_calc_bl_(nd, SCB_ALL, opt) for an NPC: status_calc_npc_, then
/// status_calc_bl_main — which returns at once for an NPC with a monster class (status_get_base_status is nullptr) —
/// with base and battle status being the same nd->status. No client update is sent for NPCs.
pub fn status_calc_npc(c: &Ctx, nd_id: u32, opt: u8) {
    let Some(nd) = c.db().npc().id().find(nd_id) else { return };
    let u = unit::get(c, nd_id);
    let size = u.as_ref().map(|u| u.size).unwrap_or(0);
    let speed = u.as_ref().map(|u| u.speed).unwrap_or(crate::npc::DEFAULT_NPC_WALK_SPEED);
    let mut m = npcm(c, nd_id);
    status_calc_npc_base(nd_id, size, speed, &mut m, opt);
    if crate::data::mobdb_checkid(nd.class) {
        put_npcm(c, &m);
        return;
    }
    let mapid = u.as_ref().map(|u| u.map).unwrap_or(0);
    let w = who(c, nd_id, mapid);
    let sc = crate::status::sc::load(c, nd_id);
    let st = &mut m.status;
    // SCB_SPEED: "Re-walk to adjust speed"
    if let Some(mut uu) = unit::get(c, nd_id) {
        uu.ud.change_walk_target = true;
        unit::save(c, &uu);
    }
    st.str = cs::status_calc_str(&w, &sc, st.str as i32) as i16;
    st.agi = cs::status_calc_agi(&w, &sc, st.agi as i32) as i16;
    st.vit = cs::status_calc_vit(&w, &sc, st.vit as i32) as i16;
    st.int_ = cs::status_calc_int(&w, &sc, st.int_ as i32) as i16;
    st.dex = cs::status_calc_dex(&w, &sc, st.dex as i32) as i16;
    st.luk = cs::status_calc_luk(&w, &sc, st.luk as i32) as i16;
    st.pow = cs::status_calc_pow(&w, &sc, st.pow as i32) as i16;
    st.sta = cs::status_calc_sta(&w, &sc, st.sta as i32) as i16;
    st.wis = cs::status_calc_wis(&w, &sc, st.wis as i32) as i16;
    st.spl = cs::status_calc_spl(&w, &sc, st.spl as i32) as i16;
    st.con = cs::status_calc_con(&w, &sc, st.con as i32) as i16;
    st.crt = cs::status_calc_crt(&w, &sc, st.crt as i32) as i16;
    let lv = m.level as i32; // status_get_lv: nd->level
    if st.batk != 0 {
        // status->batk = status_base_atk(status); temp = b_status->batk - status_base_atk(b_status) is 0 (one object)
        st.batk = status_base_atk_npc(st, lv);
        st.batk = cs::status_calc_batk(&w, &sc, st.batk);
    }
    if st.watk == 0 {
        // We only have left-hand weapon
        st.watk = 0;
        st.watk2 = cs::status_calc_watk(&w, &sc, st.watk2 as i32);
    } else {
        st.watk = cs::status_calc_watk(&w, &sc, st.watk as i32);
    }
    st.hit = cs::status_calc_hit(&w, &sc, st.hit as i32);
    st.flee = cs::status_calc_flee(&w, &sc, st.flee as i32);
    st.def = cs::status_calc_def(&w, &sc, st.def as i32);
    st.def2 = cs::status_calc_def2(&w, &sc, st.def2 as i32, st.vit as i32);
    st.mdef = cs::status_calc_mdef(&w, &sc, st.mdef as i32);
    st.mdef2 = cs::status_calc_mdef2(&w, &sc, st.mdef2 as i32);
    st.speed = cs::status_calc_speed(&w, &sc, st.speed as i32);
    if st.cri != 0 {
        st.cri = cs::status_calc_critical(&w, &sc, st.cri as i32);
    }
    if st.flee2 != 0 {
        st.flee2 = cs::status_calc_flee2(&w, &sc, st.flee2 as i32);
    }
    st.rhw.ele = cs::status_calc_attack_element(&sc, st.rhw.ele as i32);
    st.lhw.ele = cs::status_calc_attack_element(&sc, st.lhw.ele as i32);
    st.def_ele = cs::status_calc_element(&w, &sc, st.def_ele as i32);
    st.ele_lv = cs::status_calc_element_lv(&w, &sc, st.ele_lv as i32);
    st.mode = cs::status_calc_mode(&w, &sc, st.mode);
    let has = |mode: i32, bits: i32| mode & bits == bits;
    st.class_ = if has(st.mode, MD_STATUSIMMUNE | MD_SKILLIMMUNE) {
        CLASS_BATTLEFIELD
    } else if has(st.mode, MD_STATUSIMMUNE | MD_KNOCKBACKIMMUNE | MD_DETECTOR) {
        CLASS_BOSS
    } else if has(st.mode, MD_STATUSIMMUNE) {
        CLASS_GUARDIAN
    } else {
        CLASS_NORMAL
    } as u8;
    let (can_attack, can_move) = (has(st.mode, MD_CANATTACK), has(st.mode, MD_CANMOVE));
    st.max_hp = cs::status_calc_maxhp(&w, &sc, st.max_hp as u64);
    if st.hp > st.max_hp {
        st.hp = st.max_hp;
    }
    st.max_sp = cs::status_calc_maxsp(&w, &sc, st.max_sp as u64);
    if st.sp > st.max_sp {
        st.sp = st.max_sp;
    }
    // SCB_MATK (RENEWAL): StatusMATK, Recognized Spell, Mystical Amplification, pseudo-buff and consumable MATK
    let mut matk_min = status_base_matk_npc(st, lv);
    let mut matk_max = status_base_matk_npc(st, lv);
    if sc.has(crate::status::consts::SC_RECOGNIZEDSPELL) {
        matk_min = matk_min.max(matk_max);
    }
    if let Some(e) = sc.get(crate::status::consts::SC_MAGICPOWER) {
        matk_min += matk_min * e.val3 / 100;
        matk_max += matk_max * e.val3 / 100;
    }
    matk_min = cs::status_calc_pseudobuff_matk(&w, &sc, matk_min) as i32;
    matk_max = cs::status_calc_pseudobuff_matk(&w, &sc, matk_max) as i32;
    matk_min = cs::status_calc_consumablematk(&sc, matk_min) as i32;
    matk_max = cs::status_calc_consumablematk(&sc, matk_max) as i32;
    st.matk_min = cap(matk_min, 0, u16::MAX as i32) as u16;
    st.matk_max = cap(matk_max, 0, u16::MAX as i32) as u16;
    // SCB_ASPD: "Mercenary and mobs" branch (NPCs are neither players nor homunculi)
    let mut amotion = st.amotion as i32;
    st.aspd_rate = cs::status_calc_aspd_rate(&w, &sc, st.aspd_rate as i32);
    amotion = amotion * st.aspd_rate as i32 / 1000;
    amotion = cs::status_calc_fix_aspd(&w, &sc, amotion) as i32;
    st.amotion = cap(amotion, MAX_ASPD_NOPC, MIN_ASPD) as u16; // AMOTION_DIVIDER_NOPC 1
    let temp = st.adelay as i32 * (st.aspd_rate as i32).min(1000) / 1000;
    st.adelay = cap(temp, st.amotion as i32, MIN_ASPD) as u16;
    // RENEWAL trait stats
    st.patk = cs::status_calc_patk(&w, &sc, st.patk as i32);
    st.smatk = cs::status_calc_smatk(&w, &sc, st.smatk as i32);
    st.res = cs::status_calc_res(&w, &sc, st.res as i32);
    st.mres = cs::status_calc_mres(&w, &sc, st.mres as i32);
    st.hplus = cs::status_calc_hplus(&w, &sc, st.hplus as i32);
    st.crate_ = cs::status_calc_crate(&w, &sc, st.crate_ as i32);
    st.max_ap = cs::status_calc_maxap(&w, &sc, st.max_ap as u64);
    if st.ap > st.max_ap {
        st.ap = st.max_ap;
    }
    let mode = m.status.mode;
    put_npcm(c, &m);
    // Since mode changed, reset their state.
    if let Some(mut uu) = unit::get(c, nd_id) {
        // status_bl_has_mode reads nd->status.mode (the unit row's copy is what unit_walktoxy checks here)
        uu.mode = mode as u32;
        if !can_attack {
            unit::stop_attack(c, &mut uu);
        }
        if !can_move {
            unit::stop_walking(c, &mut uu, unit::USW_FIXPOS, 0);
        }
        unit::save(c, &uu);
    }
}

// ---------------------------------------------------------------------------------------------- walking NPCs

/// unit.cpp unit_walktoxy_sub (PACKETVER >= 20170726): a walking NPC with a player sprite is re-spawned as a player
/// unit (unit_refresh(bl, true)).
pub fn walktoxy_sub_start(c: &Ctx, u: &Unit) {
    if u.kind == BL_NPC && crate::clif::pcdb_checkid(u.vd.class) {
        unit_refresh(c, u, true);
    }
}

/// map_moveblock for an NPC: npc_data is its block_list, so nd->x / nd->y move with it.
pub fn npc_moved(c: &Ctx, u: &Unit) {
    if u.kind != BL_NPC {
        return;
    }
    if let Some(n) = c.db().npc().id().find(u.id) {
        if n.x != u.x || n.y != u.y {
            c.db().npc().id().update(Npc { x: u.x, y: u.y, ..n });
        }
    }
}

/// unit.cpp unit_walktoxy_timer, arrival at (to_x, to_y): a player-sprite NPC turns back into an NPC unit,
/// force_walk ends and walk_done_event runs (unitwalk's event label). The unit must be saved by the caller before;
/// returns false when the unit is gone or dead after the event (the timer stops there).
pub fn walk_arrived(c: &Ctx, id: u32) -> bool {
    let Some(mut u) = unit::get(c, id) else { return false };
    // If this was a walking NPC and it used a player sprite: respawn the NPC as NPC unit
    if u.kind == BL_NPC && crate::clif::pcdb_checkid(u.vd.class) {
        unit_refresh(c, &u, false);
    }
    // Remove any possible escape states present for mobs once they stopped moving.
    if u.kind == crate::map::BL_MOB {
        if let Some(mut md) = crate::mob::get(c, id) {
            md.can_escape = false;
            crate::mob::put(c, &md);
        }
    }
    u.ud.force_walk = false;
    if u.ud.walk_done_event.is_empty() {
        unit::save(c, &u);
        return true;
    }
    // Copying is required in case someone uses unitwalkto inside the event code
    let event: String = u.ud.walk_done_event.chars().take(crate::script::npc_run::EVENT_NAME_LENGTH - 1).collect();
    // Clear the event
    u.ud.walk_done_event.clear();
    u.ud.walk_script = true;
    unit::save(c, &u);
    // Execute the event
    crate::script::npc_run::npc_event_do_id(c, &event, id);
    let Some(mut u) = unit::get(c, id) else { return false };
    u.ud.walk_script = false;
    unit::save(c, &u);
    // Check if the unit was killed
    if status_isdead(c, &u) {
        if u.kind == crate::map::BL_MOB && crate::mob::get(c, id).map(|md| md.spawn == 0).unwrap_or(false) {
            crate::mob::unit::unit_free(c, id, crate::unit::CLR_OUTSIGHT);
        }
        return false;
    }
    true
}

/// status.hpp status_isdead (status_get_status_data(bl)->hp == 0) with an NPC's own status: nd->status unless its class
/// is a monster's (then rAthena's dummy status, hp 1).
pub fn status_isdead(c: &Ctx, u: &Unit) -> bool {
    if u.kind == BL_NPC {
        let class = c.db().npc().id().find(u.id).map(|n| n.class).unwrap_or(0);
        if crate::data::mobdb_checkid(class) {
            return false;
        }
        return npcm(c, u.id).status.hp == 0;
    }
    crate::status::sc::status_isdead(c, u)
}

/// unit.cpp unit_walktoxy_timer, BL_NPC after a step: an NPC with an OnTouch area touches the players of its map
/// (map_foreachinmap(unit_walktoxy_ontouch, nd->m, BL_PC, nd)).
pub fn walk_step_npc(c: &Ctx, u: &Unit) {
    let Some(nd) = c.db().npc().id().find(u.id) else { return };
    if nd.is_invisible {
        return;
    }
    let (xs, ys) = match nd.subtype {
        NPCTYPE_SCRIPT | NPCTYPE_WARP => (nd.xs, nd.ys),
        _ => (-1, -1),
    };
    if xs > -1 && ys > -1 {
        for pl in map::players_on_map(c, u.map) {
            if pl.kind == BL_PC {
                crate::npc::walktoxy_ontouch(c, pl.id, &nd);
            }
        }
    }
}
