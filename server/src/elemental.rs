//! map/elemental.cpp (rAthena e985006): the Sorcerer's / Elemental Master's elementals. Creation (elemental_create →
//! the char server → elemental_data_received), the summon timer (elemental_summon_init / elemental_summon_end /
//! elemental_summon_stop / elemental_get_lifetime), deletion (elemental_delete), saving (elemental_save), the modes and
//! their skills (elemental_change_mode / elemental_change_mode_ack / elemental_action / elemental_clean_effect), the
//! master's target (elemental_set_target), the server-side AI (elemental_ai_timer: the SP upkeep, following the master,
//! the aggressive mode's search and attacks), heal / death notifications, elemental_skillnotok and
//! elemental_skill_get_requirements; and the map-server side of the inter link (intif_elemental_*).
//!
//! The Sorcerer's summon skills (SO_SUMMON_*, SO_EL_*) are the mage agent's classes: they call [`elemental_create`],
//! [`elemental_delete`], [`elemental_change_mode`], [`elemental_action`] and the accessors of `data`.
//!
//! Submodules: `data` (tables), `clif` (the elemental packets), `buildins` (the script commands).

pub mod buildins;
pub mod clif;
pub mod data;

use crate::companion::MercElemInter;
use crate::int_elemental::SElemental;
use crate::intif::{self, Inter};
use crate::map::{BL_ELEM, BL_PC};
use crate::status::consts::*;
use crate::timer::{self, Func};
use crate::unit::{self, Unit, CLR_OUTSIGHT, CLR_TELEPORT};
use crate::world::{Ctx, Tick};
use data::*;

// ---------------------------------------------------------------------------------------------- intif

/// intif.cpp intif_elemental_create (0x307c).
pub fn intif_elemental_create(ele: SElemental) {
    intif::send(Inter::MercElem(MercElemInter::ElementalCreate { ele }));
}

/// intif.cpp intif_elemental_request (0x307d).
pub fn intif_elemental_request(ele_id: i32, char_id: u32) {
    intif::send(Inter::MercElem(MercElemInter::ElementalRequest { ele_id, char_id }));
}

/// intif.cpp intif_elemental_delete (0x307e).
pub fn intif_elemental_delete(ele_id: i32) {
    intif::send(Inter::MercElem(MercElemInter::ElementalDelete { ele_id }));
}

/// intif.cpp intif_elemental_save (0x307f).
pub fn intif_elemental_save(ele: SElemental) {
    intif::send(Inter::MercElem(MercElemInter::ElementalSave { ele }));
}

// ---------------------------------------------------------------------------------------------- elemental.cpp

/// elemental.cpp elemental_create: an elemental of `class_` for the player `aid`, its values derived from the caster's
/// battle status and levels (the summon level is the elemental's size + 1), the class bonuses, SO_EL_SYMPATHY and
/// EM_ELEMENTAL_SPIRIT_M (Elemental Master's elementals), and `lifetime` ms of summon time; the char server creates it
/// (elemental_data_received). Returns 0 for an unknown class, 1 otherwise (rAthena's values).
pub fn elemental_create(c: &Ctx, aid: u32, class_: i32, lifetime: u32) -> i32 {
    let Some(db) = crate::db::misc::elemental::elemental_db().find(class_) else {
        log::error!("elemental_create: Unknown elemental class {class_}.");
        return 0;
    };
    let (Some(ch), Some(sd)) = (crate::pc::character_of(c, aid), crate::status::data::sd_get(c, aid)) else { return 1 };
    let bs = &sd.battle_status;
    let mut ele = SElemental { char_id: ch.id, class_: class_ as i16, mode: EL_MODE_PASSIVE, ..Default::default() }; // Initial mode
    let mut i: i32 = db.status.size as i32 + 1; // summon level
    //[(Caster's Max HP/ 3 ) + (Caster's INT x 10 )+ (Caster's Job Level x 20 )] x [(Elemental Summon Level + 2) / 3]
    ele.max_hp = (bs.max_hp as i32 / 3 + bs.int_ as i32 * 10 + ch.job_level as i32 * 20) * ((i + 2) / 3);
    ele.hp = ele.max_hp;
    //Caster's Max SP /4
    ele.max_sp = bs.max_sp as i32 / 4;
    ele.sp = ele.max_sp;
    //Caster's [ Max SP / (18 / Elemental Summon Skill Level) 1- 100 ]
    ele.atk = bs.max_sp as i32 / (18 / i) - 100;
    //Caster's [ Max SP / (18 / Elemental Summon Skill Level) ]
    ele.atk2 = bs.max_sp as i32 / (18 / i);
    //Caster's HIT + (Caster's Base Level)
    ele.hit = (bs.hit as i32 + ch.base_level as i32) as i16;
    //[Elemental Summon Skill Level x (Caster's INT / 2 + Caster's DEX / 4)]
    ele.matk = i * (bs.int_ as i32 / 2 + bs.dex as i32 / 4);
    //150 + [Caster's DEX / 10] + [Elemental Summon Skill Level x 3 ]
    ele.amotion = (150 + bs.dex as i32 / 10 + i * 3) as i16;
    //Caster's DEF + (Caster's Base Level / (5 - Elemental Summon Skill Level)
    ele.def = (bs.def as i32 + ch.base_level as i32 / (5 - i)) as i16;
    //Caster's MDEF + (Caster's INT / (5 - Elemental Summon Skill Level)
    ele.mdef = (bs.mdef as i32 + bs.int_ as i32 / (5 - i)) as i16;
    //Caster's FLEE + (Caster's Base Level / (5 - Elemental Summon Skill Level)
    ele.flee = (bs.flee as i32 + ch.base_level as i32 / (5 - i)) as i16;
    //per individual bonuses
    match db.class_ {
        ELEMENTALID_AGNI_S | ELEMENTALID_AGNI_M | ELEMENTALID_AGNI_L | ELEMENTALID_ARDOR => {
            //ATK + (Summon Agni Skill Level x 20) / HIT + (Summon Agni Skill Level x 10)
            ele.atk += i * 20;
            ele.atk2 += i * 20;
            ele.hit = (ele.hit as i32 + i * 10) as i16;
        }
        ELEMENTALID_AQUA_S | ELEMENTALID_AQUA_M | ELEMENTALID_AQUA_L | ELEMENTALID_DILUVIO => {
            //MDEF + (Summon Aqua Skill Level x 10) / MATK + (Summon Aqua Skill Level x 20)
            ele.mdef = (ele.mdef as i32 + i * 10) as i16;
            ele.matk += i * 20;
        }
        ELEMENTALID_VENTUS_S | ELEMENTALID_VENTUS_M | ELEMENTALID_VENTUS_L | ELEMENTALID_PROCELLA => {
            //FLEE + (Summon Ventus Skill Level x 20) / MATK + (Summon Ventus Skill Level x 10)
            ele.flee = (ele.flee as i32 + i * 20) as i16;
            ele.matk += i * 10;
        }
        ELEMENTALID_TERA_S | ELEMENTALID_TERA_M | ELEMENTALID_TERA_L | ELEMENTALID_TERREMOTUS | ELEMENTALID_SERPENS => {
            //DEF + (Summon Tera Skill Level x 25) / ATK + (Summon Tera Skill Level x 5)
            ele.def = (ele.def as i32 + i * 25) as i16;
            ele.atk += i * 5;
            ele.atk2 += i * 5;
        }
        _ => {}
    }
    i = crate::status::sc_ext::pc_checkskill(c, aid, crate::db::skills::ids::SO_EL_SYMPATHY);
    if i > 0 {
        ele.max_hp += ele.max_hp * 5 * i / 100;
        ele.hp = ele.max_hp;
        ele.max_sp += ele.max_sp * 5 * i / 100;
        ele.sp = ele.max_sp;
        ele.atk += 25 * i;
        ele.atk2 += 25 * i;
        ele.matk += 25 * i;
    }
    i = crate::status::sc_ext::pc_checkskill(c, aid, crate::db::skills::ids::EM_ELEMENTAL_SPIRIT_M);
    if i > 0 && db.class_ >= ELEMENTALID_DILUVIO && db.class_ <= ELEMENTALID_SERPENS {
        ele.max_hp += 10000 + 3000 * i;
        ele.hp = ele.max_hp;
        ele.max_sp += 100 * i;
        ele.sp = ele.max_sp;
        ele.atk += 100 + 20 * i;
        ele.atk2 += 100 + 20 * i;
        ele.matk += 20 * i;
        ele.def = (ele.def as i32 + 20 * i) as i16;
        ele.mdef = (ele.mdef as i32 + 4 * i) as i16;
        ele.flee = (ele.flee as i32 + 10 * i) as i16;
    }
    ele.life_time = lifetime as i64;
    // Request Char Server to create this elemental
    intif_elemental_create(ele);
    1
}

/// elemental.cpp elemental_get_lifetime: the summon time left (0 without a summon timer).
pub fn elemental_get_lifetime(c: &Ctx, ed: &ElementalData) -> i64 {
    if ed.summon_timer == 0 {
        return 0;
    }
    timer::due(c, ed.summon_timer).map(|due| due - c.tick()).unwrap_or(0)
}

/// elemental.cpp elemental_save: the battle values (mode, HP / SP, max HP / SP, ATK, ATK2, MATK min, DEF, MDEF, FLEE,
/// HIT) and the summon time left go to the char server.
pub fn elemental_save(c: &Ctx, id: u32) -> i32 {
    let Some(mut ed) = get(c, id) else { return 0 };
    let bs = ed.battle_status.clone();
    ed.elemental.mode = bs.mode;
    ed.elemental.hp = bs.hp as i32;
    ed.elemental.sp = bs.sp as i32;
    ed.elemental.max_hp = bs.max_hp as i32;
    ed.elemental.max_sp = bs.max_sp as i32;
    ed.elemental.atk = bs.rhw.atk as i32;
    ed.elemental.atk2 = bs.rhw.atk2 as i32;
    ed.elemental.matk = bs.matk_min as i32;
    ed.elemental.def = bs.def;
    ed.elemental.mdef = bs.mdef;
    ed.elemental.flee = bs.flee;
    ed.elemental.hit = bs.hit;
    ed.elemental.life_time = elemental_get_lifetime(c, &ed);
    put(c, &ed);
    intif_elemental_save(ed.elemental.clone());
    1
}

/// elemental.cpp elemental_summon_end (Func::ElementalSummonEnd; target = the master's account id): the summon time is
/// over.
pub fn elemental_summon_end(c: &Ctx, tid: u64, aid: u32) -> i32 {
    if crate::pc::get(c, aid).is_none() {
        return 1;
    }
    let Some(mut ed) = of_master(c, aid) else { return 1 };
    if ed.summon_timer != tid {
        log::error!("elemental_summon_end {} != {}.", ed.summon_timer, tid);
        return 0;
    }
    ed.summon_timer = 0;
    put(c, &ed);
    elemental_delete(c, ed.id); // Elemental's summon time is over.
    0
}

/// elemental.cpp elemental_summon_stop: the summon timer ends.
pub fn elemental_summon_stop(c: &Ctx, id: u32) {
    let Some(mut ed) = get(c, id) else { return };
    if ed.summon_timer != 0 {
        timer::delete_checked(c, ed.summon_timer, Func::ElementalSummonEnd);
    }
    ed.summon_timer = 0;
    put(c, &ed);
}

/// elemental.cpp elemental_delete: the elemental's options end (on it and its master), the summon ends, the master
/// loses it (sd->ed, ele_id 0) and it leaves the map (unit_remove_map frees it: it has no time left). Without a master
/// it is freed at once.
pub fn elemental_delete(c: &Ctx, id: u32) -> i32 {
    let Some(mut ed) = get(c, id) else { return 0 };
    ed.elemental.life_time = 0;
    put(c, &ed);
    elemental_clean_effect(c, id);
    elemental_summon_stop(c, id);
    let Some(ed) = get(c, id) else { return 0 };
    if crate::pc::get(c, ed.master).is_none() {
        return crate::companion::unit::unit_free(c, id, CLR_OUTSIGHT);
    }
    // sd->ed = nullptr (the row keeps its master until unit_free; ed->master is still set in rAthena too)
    set_ele_id(c, ed.master, 0);
    detach(c, id);
    if !unit::get(c, id).map(|u| u.on_map).unwrap_or(false) {
        // unit_remove_map returns at once for an elemental off the map: rAthena leaves the object in memory,
        // unreachable (sd->ed is null: no AI, and its regen reaches no client). Deliberately different: its rows go
        // (the table keys it by its master), with no packet and no char-server request, as rAthena sends none.
        crate::status::sc::free_unit(c, id);
        c.db().elemental_data().id().delete(id);
        c.db().unit().id().delete(id);
        return 0;
    }
    unit::remove_map(c, id, CLR_OUTSIGHT) as i32
}

/// `sd->ed = nullptr`: the player no longer has this elemental (ed->master still points at him until unit_free).
fn detach(c: &Ctx, id: u32) {
    if let Some(mut ed) = get(c, id) {
        ed.detached = true;
        c.db().elemental_data().id().update(ed);
    }
}

/// ed->master of an elemental (its master's account id, also after elemental_delete's `sd->ed = nullptr`).
pub fn master_of(_c: &Ctx, ed: &ElementalData) -> u32 {
    ed.master
}

/// elemental.cpp elemental_summon_init: the summon timer starts (life_time from now), regen is unblocked.
fn elemental_summon_init(c: &Ctx, ed: &mut ElementalData) {
    if ed.summon_timer == 0 {
        ed.summon_timer = timer::add(c, c.tick() + ed.elemental.life_time, Func::ElementalSummonEnd, ed.master, 0);
    }
    ed.regen.block = 0;
}

/// elemental.cpp elemental_data_received: the char server created or loaded an elemental. A new one gets its object
/// (an NPC id, the view data with look[LOOK_HEAD_MID] = 10, the master's direction, a cell next to him, regen ticks,
/// map_addiddb, the first status calculation, the think / SP drain ticks, the summon timer); an existing one takes the
/// new data. With the master on a map it is spawned and shown (ZC_EL_INIT, ZC_EL_PAR_CHANGE HP, the HP meter, SP).
pub fn elemental_data_received(c: &Ctx, ele: SElemental, flag: bool) -> i32 {
    let tick = c.tick();
    let Some(aid) = crate::pc::charid2sd(c, ele.char_id).map(|u| u.id) else { return 0 };
    let db = crate::db::misc::elemental::elemental_db().find(ele.class_ as i32);
    if !flag || db.is_none() {
        // Not created - loaded - DB info
        set_ele_id(c, aid, 0);
        return 0;
    }
    let Some(sdu) = unit::get(c, aid) else { return 0 };
    let id = match of_master(c, aid) {
        None => {
            // Initialize it after first summon.
            let id = crate::mob::spawn::npc_get_new_npc_id(c);
            let mut u = Unit {
                id,
                kind: BL_ELEM,
                map: sdu.map,
                x: sdu.x,
                y: sdu.y,
                on_map: false,
                name: String::new(),
                vd: Default::default(),
                ud: Default::default(),
                sc: Default::default(),
                speed: crate::status::defs::DEFAULT_WALK_SPEED as u16,
                mode: 0,
                level: 1,
                char_id: 0,
                head_dir: 0,
                font: 0,
                manner: 0,
                karma: false,
                guild_id: 0,
                emblem_id: 0,
                hp: 0,
                max_hp: 0,
                dead: false,
                size: 0,
                boss: 0,
                invisible: false,
                npc_owner_char: 0,
            };
            // unit_dataset: the unit_data starts empty but for the ticks (now) and the sub-cell (8, 8); the direction of
            // the master (md->ud.dir = sd->ud.dir)
            u.ud.attackabletime = tick;
            u.ud.canact_tick = tick;
            u.ud.canmove_tick = tick;
            u.ud.endure_tick = tick;
            u.ud.sx = 8;
            u.ud.sy = 8;
            u.ud.dir = sdu.ud.dir;
            c.db().unit().insert(u);
            let ed = ElementalData {
                id,
                master: aid,
                db_class: ele.class_ as i32,
                elemental: ele.clone(),
                base_status: Default::default(),
                battle_status: Default::default(),
                regen: Default::default(),
                // Ticks need to be initialized before adding bl to map_addiddb
                regen_tick_hp: tick,
                regen_tick_sp: tick,
                masterteleport_timer: 0,
                summon_timer: 0,
                skill_timer: 0,
                last_thinktime: 0,
                last_linktime: 0,
                last_spdrain_time: 0,
                min_chase: 0,
                target_id: 0,
                attacked_id: 0,
                detached: false,
            };
            put(c, &ed);
            crate::companion::status::status_set_viewdata(c, id, ele.class_ as i32);
            if let Some(mut u) = unit::get(c, id) {
                u.vd.head_mid = 10; // TODO: Why?
                unit::save(c, &u);
            }
            crate::pet::ai::unit_calc_pos(c, id, sdu.x as i32, sdu.y as i32, sdu.ud.dir);
            if let Some(mut u) = unit::get(c, id) {
                u.x = u.ud.to_x;
                u.y = u.ud.to_y;
                unit::save(c, &u);
            }
            // map_addiddb: the elemental is in regen_db (natural heal)
            crate::status::hp::ensure_natural_heal_timer(c);
            ensure_ai_timer(c);
            crate::status::calc_bl(c, id, crate::db::misc::status::status_db().get_scb_all(), crate::status::SCO_FIRST);
            if let Some(mut ed) = get(c, id) {
                ed.last_spdrain_time = c.tick();
                ed.last_thinktime = c.tick();
                ed.summon_timer = 0;
                ed.masterteleport_timer = 0;
                elemental_summon_init(c, &mut ed);
                put(c, &ed);
            }
            id
        }
        Some(mut ed) => {
            ed.elemental = ele.clone();
            put(c, &ed);
            ed.id
        }
    };
    set_ele_id(c, aid, ele.elemental_id as u32);
    let on_map = unit::get(c, id).map(|u| u.on_map).unwrap_or(true);
    if !on_map && unit::get(c, aid).map(|u| u.on_map).unwrap_or(false) {
        let Some(mut u) = unit::get(c, id) else { return 0 };
        if !unit::addblock(c, &mut u) {
            return 0;
        }
        crate::clif::spawn(c, &u, false);
        clif::clif_elemental_info(c, aid);
        clif::clif_elemental_updatestatus(c, aid, SP_HP);
        if let Some(ed) = get(c, id) {
            clif::clif_hpmeter_single(c, aid, ed.id, ed.battle_status.hp, ed.battle_status.max_hp);
        }
        clif::clif_elemental_updatestatus(c, aid, SP_SP);
    }
    1
}

/// elemental.cpp elemental_clean_effect: the statuses flagged SCF_REMOVEELEMENTALOPTION end on the elemental and on its
/// master (battle_get_master).
pub fn elemental_clean_effect(c: &Ctx, id: u32) -> i32 {
    let Some(ed) = get(c, id) else { return 0 };
    let has = |t| crate::status::sc::has_sce(c, id, t);
    for t in crate::db::misc::status::status_db().remove_by_status_flag(&has, &[SCF_REMOVEELEMENTALOPTION]) {
        crate::status::sc::status_change_end(c, id, t, 0);
    }
    // battle_get_master(ed): the master (or the elemental itself without one)
    let m = master_of(c, &ed);
    let target = if m != 0 && unit::get(c, m).is_some() { m } else { id };
    let has = |t| crate::status::sc::has_sce(c, target, t);
    for t in crate::db::misc::status::status_db().remove_by_status_flag(&has, &[SCF_REMOVEELEMENTALOPTION]) {
        crate::status::sc::status_change_end(c, target, t, 0);
    }
    1
}

/// elemental.cpp elemental_action: the aggressive mode's skill on `target` (in range: its HP / SP cost is paid by the
/// elemental unless the master just used SO_EL_ACTION, then it is cast; out of range: the elemental walks towards it
/// and a castend timer fires when it should arrive). 0: nothing done.
pub fn elemental_action(c: &Ctx, id: u32, bl: &Unit, tick: Tick) -> i32 {
    let Some(ed) = get(c, id) else { return 0 };
    if crate::pc::get(c, ed.master).is_none() {
        return 0;
    }
    if ed.target_id != 0 {
        elemental_unlocktarget(c, id); // Remove previous target.
    }
    let Some(db) = db_of(&ed) else { return 0 };
    // only one skill per mode is supported
    let Some(skill) = db.skill_for(EL_SKILLMODE_AGGRESSIVE).copied() else { return 0 };
    let (skill_id, skill_lv) = (skill.id, skill.lv);
    if elemental_skillnotok(c, skill_id, id) {
        return 0;
    }
    let Some(u) = unit::get(c, id) else { return 0 };
    if u.ud.skilltimer != 0 {
        return 0;
    } else if tick - u.ud.canact_tick < 0 {
        return 0;
    }
    if let Some(mut ed) = get(c, id) {
        ed.target_id = bl.id; // Set new target
        ed.last_thinktime = tick;
        put(c, &ed);
    }
    if let Some(mut u) = unit::get(c, id) {
        u.ud.skilltarget = bl.id;
        unit::save(c, &u);
    }
    let range = crate::db::skills::skill_get_range(skill_id, skill_lv);
    let Some(u) = unit::get(c, id) else { return 0 };
    // Not in skill range.
    if !crate::battle::target::battle_check_range(c, &u, bl, range) {
        // Try to walk to the target.
        if !unit::walktobl(c, id, bl, range, 2) {
            elemental_unlocktarget(c, id);
        } else {
            // Walking, waiting to be in range. Client don't handle it, then we must handle it here.
            let Some(mut u) = unit::get(c, id) else { return 1 };
            let walk_dist = crate::mob::unit::distance_bl(&u, bl) - range;
            u.ud.skill_id = skill_id;
            u.ud.skill_lv = skill_lv;
            let due = tick + unit::speed(&u) * walk_dist as i64;
            u.ud.skilltimer = if crate::db::skills::skill_get_inf(skill_id) & crate::db::skills::INF_GROUND_SKILL as i32 != 0 {
                timer::add(c, due, Func::SkillCastendPos, id, 0)
            } else {
                timer::add(c, due, Func::SkillCastendId, id, 0)
            };
            unit::save(c, &u);
        }
        return 1;
    }
    let req = elemental_skill_get_requirements(skill_id, skill_lv);
    if req.0 != 0 || req.1 != 0 {
        // BL_CAST(BL_PC, battle_get_master(ed))
        let master = get(c, id).map(|ed| master_of(c, &ed)).unwrap_or(0);
        if crate::pc::get(c, master).is_some() {
            let old = crate::skill::state::get(c, master).skill_id_old;
            let st = crate::companion::status_of(c, id).unwrap_or_default();
            if old != crate::db::skills::ids::SO_EL_ACTION && (st.hp < req.0 as u32 || st.sp < req.1 as u32) {
                //regardless of remaining HP/SP it can be cast
                return 1;
            } else {
                crate::status::hp::status_zap(c, id, req.0 as i64, req.1 as i64, 0);
            }
        }
    }
    //Otherwise, just cast the skill.
    if crate::db::skills::skill_get_inf(skill_id) & crate::db::skills::INF_GROUND_SKILL as i32 != 0 {
        crate::skill::unit::unit_skilluse_pos(c, id, bl.x, bl.y, skill_id, skill_lv);
    } else {
        crate::skill::unit::unit_skilluse_id(c, id, bl.id, skill_id, skill_lv);
    }
    // Reset target.
    if let Some(mut ed) = get(c, id) {
        ed.target_id = 0;
        put(c, &ed);
    }
    1
}

/// elemental.cpp elemental_change_mode_ack: the skill of the new mode, cast on the master (a ground skill at his cell).
pub fn elemental_change_mode_ack(c: &Ctx, id: u32, skill_mode: u8) -> i32 {
    let Some(ed) = get(c, id) else { return 0 };
    let master = master_of(c, &ed);
    let Some(bl) = unit::get(c, master) else { return 0 };
    let Some(db) = db_of(&ed) else { return 0 };
    let Some(skill) = db.skill_for(skill_mode).copied() else { return 0 };
    let (skill_id, skill_lv) = (skill.id, skill.lv);
    if elemental_skillnotok(c, skill_id, id) {
        return 0;
    }
    let Some(u) = unit::get(c, id) else { return 0 };
    if u.ud.skilltimer != 0 {
        return 0;
    } else if c.tick() - u.ud.canact_tick < 0 {
        return 0;
    }
    if let Some(mut ed) = get(c, id) {
        ed.target_id = bl.id; // Set new target
        ed.last_thinktime = c.tick();
        put(c, &ed);
    }
    if crate::db::skills::skill_get_inf(skill_id) & crate::db::skills::INF_GROUND_SKILL as i32 != 0 {
        crate::skill::unit::unit_skilluse_pos(c, id, bl.x, bl.y, skill_id, skill_lv);
    } else {
        crate::skill::unit::unit_skilluse_id(c, id, bl.id, skill_id, skill_lv);
    }
    if let Some(mut ed) = get(c, id) {
        ed.target_id = 0; // Reset target after casting the skill  to avoid continious attack.
        put(c, &ed);
    }
    1
}

/// elemental.cpp elemental_change_mode: the elemental drops its target, the previous mode's effects end (when the mode
/// changes), the new mode is set (battle status and saved data), and the passive / assist modes cast their skill at
/// once (elemental_change_mode_ack).
pub fn elemental_change_mode(c: &Ctx, id: u32, mode: i32) -> i32 {
    if get(c, id).is_none() {
        return 0;
    }
    // Remove target
    elemental_unlocktarget(c, id);
    // Removes the effects of the previous mode.
    if get(c, id).map(|ed| ed.elemental.mode != mode).unwrap_or(false) {
        elemental_clean_effect(c, id);
    }
    let Some(mut ed) = get(c, id) else { return 0 };
    ed.battle_status.mode = mode;
    ed.elemental.mode = mode;
    put(c, &ed);
    // Normalize elemental mode to elemental skill mode.
    let skill_mode = if mode == EL_MODE_AGGRESSIVE {
        EL_SKILLMODE_AGGRESSIVE // Aggressive spirit mode -> Aggressive spirit skill.
    } else if mode == EL_MODE_ASSIST {
        EL_SKILLMODE_ASSIST // Assist spirit mode -> Assist spirit skill.
    } else {
        EL_SKILLMODE_PASSIVE // Passive spirit mode -> Passive spirit skill.
    };
    // Use a skill inmediately after every change mode.
    if skill_mode != EL_SKILLMODE_AGGRESSIVE {
        return elemental_change_mode_ack(c, id, skill_mode);
    }
    1
}

/// elemental.cpp elemental_heal: the master sees the elemental's new HP / SP.
pub fn elemental_heal(c: &Ctx, master: u32, hp: i32, sp: i32) {
    if crate::pc::get(c, master).is_none() {
        return;
    }
    if hp != 0 {
        clif::clif_elemental_updatestatus(c, master, SP_HP);
    }
    if sp != 0 {
        clif::clif_elemental_updatestatus(c, master, SP_SP);
    }
}

/// elemental.cpp elemental_dead: the elemental is deleted; 0 for status_damage ("death cancelled").
pub fn elemental_dead(c: &Ctx, id: u32) -> i32 {
    elemental_delete(c, id);
    0
}

/// elemental.cpp elemental_unlocktarget: no target, no attack, the walk stops (USW_FIXPOS).
pub fn elemental_unlocktarget(c: &Ctx, id: u32) -> i32 {
    if let Some(mut ed) = get(c, id) {
        ed.target_id = 0;
        put(c, &ed);
    }
    if let Some(mut u) = unit::get(c, id) {
        unit::stop_attack(c, &mut u);
        unit::stop_walking(c, &mut u, unit::USW_FIXPOS, 0);
        unit::save(c, &u);
    }
    0
}

/// elemental.cpp elemental_skillnotok: an unknown skill is "ok" (false); otherwise the master's skill_isNotOk decides
/// (true without a master).
pub fn elemental_skillnotok(c: &Ctx, skill_id: u16, id: u32) -> bool {
    if crate::db::skills::skill_db().find(skill_id).is_none() {
        // skill_get_index(skill_id) == 0
        return false;
    }
    // Check if it's ok for master as well
    let master = get(c, id).map(|ed| master_of(c, &ed)).unwrap_or(0);
    match unit::get(c, master).filter(|u| u.kind == BL_PC) {
        Some(sd) => crate::skill::condition::skill_is_not_ok(c, &sd, skill_id),
        None => true,
    }
}

/// elemental.cpp elemental_skill_get_requirements: (hp, sp) cost of the skill level (0, 0 for an unknown skill).
pub fn elemental_skill_get_requirements(skill_id: u16, skill_lv: u16) -> (i32, i32) {
    let Some(skill) = crate::db::skills::skill_db().find(skill_id) else { return (0, 0) };
    let lv = skill_lv.clamp(1, crate::db::skills::MAX_SKILL_LEVEL as u16) as usize;
    (skill.require.hp[lv - 1], skill.require.sp[lv - 1])
}

/// elemental.cpp elemental_set_target (pc_damage: `if( sd->status.ele_id > 0 ) elemental_set_target(sd, src)`): a
/// visible attacker within the elemental's skill range becomes its target when it has none. 1: set / kept.
pub fn elemental_set_target(c: &Ctx, aid: u32, bl: &Unit) -> i32 {
    let Some(ed) = of_master(c, aid) else { return 0 };
    let Some(eu) = unit::get(c, ed.id) else { return 0 };
    let Some(db) = db_of(&ed) else { return 0 };
    if eu.map != bl.map || !crate::mob::unit::check_distance_bl(&eu, bl, db.range2 as i32) {
        return 0;
    }
    if !crate::status::check::status_check_skilluse(c, Some(&eu), Some(bl), 0, 0) {
        return 0;
    }
    if ed.target_id == 0 {
        let mut ed = ed;
        ed.target_id = bl.id;
        put(c, &ed);
    }
    1
}

// ---------------------------------------------------------------------------------------------- AI

/// elemental.cpp do_init_elemental: `add_timer_interval(gettick()+MIN_ELETHINKTIME, elemental_ai_timer, 0, 0,
/// MIN_ELETHINKTIME)`. The module has no server start: the interval timer is created with the first elemental (and
/// again if it is ever gone); without elementals elemental_ai_timer does nothing, so only its phase differs, which is
/// arbitrary on rAthena too.
pub fn ensure_ai_timer(c: &Ctx) {
    let s = c.db().elemental_ai_state().key().find(0);
    if let Some(s) = &s {
        if s.timer != 0 && timer::get(c, s.timer).is_some() {
            return;
        }
    }
    let t = timer::add_interval(c, c.tick() + MIN_ELETHINKTIME, Func::ElementalAiTimer, 0, 0, MIN_ELETHINKTIME);
    let row = ElementalAiState { key: 0, timer: t };
    if s.is_some() {
        c.db().elemental_ai_state().key().update(row);
    } else {
        c.db().elemental_ai_state().insert(row);
    }
}

/// elemental.cpp elemental_ai_timer (Func::ElementalAiTimer): map_foreachpc (pc_db's order) → elemental_ai_sub_foreachclient.
pub fn elemental_ai_timer(c: &Ctx, tick: Tick) {
    use crate::pc::pc as _;
    use spacetimedb::Table;
    if c.db().elemental_data().count() == 0 {
        if let Some(s) = c.db().elemental_ai_state().key().find(0) {
            if s.timer != 0 {
                timer::delete(c, s.timer);
                c.db().elemental_ai_state().key().update(ElementalAiState { key: 0, timer: 0 });
            }
        }
        return;
    }
    let mut pcs: Vec<u32> = c.db().pc().iter().map(|p| p.account_id).collect();
    pcs.sort_by_key(|&aid| crate::party::idb_order(aid));
    for aid in pcs {
        // elemental_ai_sub_foreachclient
        if ele_id_of(c, aid) != 0 {
            if let Some(ed) = of_master(c, aid) {
                elemental_ai_sub_timer(c, ed.id, aid, tick);
            }
        }
    }
}

/// elemental.cpp elemental_ai_sub_timer_activesearch over BL_CHAR within `view_range` (map_foreachinallrange): the
/// enemies it may attack in its skill range (players only on PvP maps); the last one closer than the current pick
/// wins. Returns the target (ed->target_id / min_chase are set).
fn elemental_ai_activesearch(c: &Ctx, id: u32, eu: &Unit, view_range: i32) -> Option<Unit> {
    let db = get(c, id).and_then(|ed| db_of(&ed))?;
    let mut target: Option<Unit> = None;
    for bl in crate::map::units_in_range(c, eu.map, eu.x, eu.y, view_range as i16, crate::map::BL_CHAR) {
        //If can't seek yet, not an enemy, or you can't attack it, skip.
        if target.as_ref().map(|t| t.id) == Some(bl.id) || !crate::status::check::status_check_skilluse(c, Some(eu), Some(&bl), 0, 0) {
            continue;
        }
        if crate::battle::target::battle_check_target(c, eu, &bl, crate::status::defs::BCT_ENEMY) <= 0 {
            continue;
        }
        if bl.kind == BL_PC && !crate::npc::mapflag_vs(c, eu.map) {
            continue;
        }
        let dist = crate::mob::unit::distance_bl(eu, &bl);
        let closer = match &target {
            None => true,
            Some(t) => !crate::mob::unit::check_distance_bl(eu, t, dist),
        };
        if closer && crate::battle::target::battle_check_range(c, eu, &bl, db.range2 as i32) {
            //Pick closest target?
            if let Some(mut ed) = get(c, id) {
                ed.target_id = bl.id;
                ed.min_chase = (dist + db.range3 as i32).min(crate::conf::area_size() as i32) as i16;
                put(c, &ed);
            }
            target = Some(bl);
        }
    }
    target
}

/// elemental.cpp elemental_ai_sub_timer: every 10 s the master pays the elemental's SP upkeep (5, 8 for M, 11 for L
/// elementals; without enough SP the elemental is deleted); then, at most every MIN_ELETHINKTIME and while it is not
/// casting or just starting a walk, it follows its master (warped back beyond AREA_SIZE, walks back beyond
/// MAX_ELEDISTANCE) and, in the aggressive mode, searches a target, sometimes (2%) uses its attack skill, attacks in
/// range or chases.
fn elemental_ai_sub_timer(c: &Ctx, id: u32, aid: u32, tick: Tick) -> i32 {
    let (Some(eu), Some(sd)) = (unit::get(c, id), unit::get(c, aid)) else { return 0 };
    if !eu.on_map || !sd.on_map {
        return 0;
    }
    let Some(mut ed) = get(c, id) else { return 0 };
    // Check if caster can sustain the summoned elemental
    if tick - ed.last_spdrain_time >= 10000 {
        // Drain SP every 10 seconds
        let sp = match eu.vd.class {
            ELEMENTALID_AGNI_M | ELEMENTALID_AQUA_M | ELEMENTALID_VENTUS_M | ELEMENTALID_TERA_M => 8,
            ELEMENTALID_AGNI_L | ELEMENTALID_AQUA_L | ELEMENTALID_VENTUS_L | ELEMENTALID_TERA_L => 11,
            _ => 5,
        };
        let msp = crate::status::status_get_status_data(c, &sd).sp;
        if (msp as i32) < sp {
            // Can't sustain delete it.
            elemental_delete(c, id);
            return 0;
        }
        crate::status::hp::status_zap(c, aid, 0, sp as i64, 0);
        let Some(mut e2) = get(c, id) else { return 0 };
        e2.last_spdrain_time = tick;
        put(c, &e2);
        ed = e2;
    }
    if tick - ed.last_thinktime < MIN_ELETHINKTIME {
        return 0;
    }
    ed.last_thinktime = tick;
    put(c, &ed);
    let Some(eu) = unit::get(c, id) else { return 0 };
    if eu.ud.skilltimer != 0 {
        return 0;
    }
    if eu.ud.walktimer != 0 && eu.ud.path_pos <= 2 {
        return 0; //No thinking when you just started to walk.
    }
    if eu.ud.path_pos < eu.ud.path_len && eu.ud.target == aid {
        return 0; //No thinking until be near the master.
    }
    let Some(db) = db_of(&ed) else { return 0 };
    let view_range = if crate::status::sc::has_sce(c, id, SC_BLIND) { 3 } else { db.range2 as i32 };
    let mode = crate::status::status_get_status_data(c, &eu).mode;
    let Some(sd) = unit::get(c, aid) else { return 0 };
    let master_dist = crate::mob::unit::distance_bl(&sd, &eu);
    if master_dist > crate::conf::area_size() as i32 {
        // Master out of vision range.
        elemental_unlocktarget(c, id);
        crate::companion::unit::unit_warp(c, id, sd.map as i32, sd.x, sd.y, CLR_TELEPORT);
        clif::clif_elemental_updatestatus(c, aid, SP_HP);
        clif::clif_elemental_updatestatus(c, aid, SP_SP);
        return 0;
    } else if master_dist > MAX_ELEDISTANCE {
        // Master too far, chase.
        if get(c, id).map(|ed| ed.target_id != 0).unwrap_or(false) {
            elemental_unlocktarget(c, id);
        }
        let Some(eu) = unit::get(c, id) else { return 0 };
        if eu.ud.walktimer != 0 && eu.ud.target == aid {
            return 0; //Already walking to him
        }
        if tick - eu.ud.canmove_tick < 0 {
            return 0; //Can't move yet.
        }
        if let Some((x, y)) = crate::mob::spawn::map_search_freecell(c, Some((eu.map, eu.x, eu.y)), sd.map as i32, sd.x, sd.y, MIN_ELEDISTANCE as i16, MIN_ELEDISTANCE as i16, 1, 50) {
            if unit::walktoxy(c, id, x, y, 0) {
                return 0;
            }
        }
    }
    if mode == EL_MODE_AGGRESSIVE {
        let Some(eu) = unit::get(c, id) else { return 0 };
        let mut target = if eu.ud.target != 0 { unit::get(c, eu.ud.target) } else { None };
        if target.is_none() {
            target = elemental_ai_activesearch(c, id, &eu, view_range);
        }
        let Some(target) = target else {
            //No targets available.
            elemental_unlocktarget(c, id);
            return 1;
        };
        let Some(eu) = unit::get(c, id) else { return 0 };
        if crate::battle::target::battle_check_range(c, &eu, &target, view_range) && c.rnd_chance(2, 100) {
            // 2% chance to cast attack skill.
            if elemental_action(c, id, &target, tick) != 0 {
                return 1;
            }
        }
        //Attempt to attack.
        //At this point we know the target is attackable, we just gotta check if the range matches.
        let Some(eu) = unit::get(c, id) else { return 0 };
        if eu.ud.target == target.id && eu.ud.attacktimer != 0 {
            //Already locked.
            return 1;
        }
        let range = get(c, id).map(|ed| ed.base_status.rhw.range as i32).unwrap_or(0);
        if crate::battle::target::battle_check_range(c, &eu, &target, range) {
            //Target within range, engage
            crate::battle::unit_attack::unit_attack(c, id, target.id, 1);
            return 1;
        }
        //Follow up if possible.
        if !unit::walktobl(c, id, &target, range, 2) {
            elemental_unlocktarget(c, id);
        }
    }
    0
}

// ---------------------------------------------------------------------------------------------- the master's hooks

/// pc.cpp pc_reg_received: `if( sd->status.ele_id > 0 ) intif_elemental_request(sd->status.ele_id, sd->status.char_id)`,
/// and the char server's answer (mapif_parse_elemental_load → elemental_data_received), answered in place after the
/// mercenary's (see mercenary::pc_reg_received).
pub fn pc_reg_received(c: &Ctx, aid: u32) {
    let Some(ch) = crate::pc::character_of(c, aid) else { return };
    if ch.elemental_id > 0 {
        let (flag, ele) = crate::int_elemental::mapif_elemental_load(c, ch.elemental_id as i32, ch.id);
        elemental_data_received(c, ele, flag);
    }
}

/// chrif.cpp chrif_save: `if( sd->ed && elemental_get_lifetime(sd->ed) > 0 ) elemental_save(sd->ed)`.
pub fn chrif_save(c: &Ctx, aid: u32) {
    if let Some(ed) = of_master(c, aid) {
        if elemental_get_lifetime(c, &ed) > 0 {
            elemental_save(c, ed.id);
        }
    }
}

/// pc.cpp pc_dead / pc_jobchange: `if( sd->ed ) elemental_delete(sd->ed)`.
pub fn master_drops(c: &Ctx, aid: u32) {
    if let Some(ed) = of_master(c, aid) {
        elemental_delete(c, ed.id);
    }
}

/// map.cpp map_quit: `if (sd->ed) elemental_clean_effect(sd->ed)` ("Remove effects here rather than
/// unit_remove_map_pc so we don't clear on Teleport/map change").
pub fn map_quit(c: &Ctx, aid: u32) {
    if let Some(ed) = of_master(c, aid) {
        elemental_clean_effect(c, ed.id);
    }
}

/// clif.cpp clif_parse_LoadEndAck's elemental part (clif.cpp:10893-10902): back on the map with its spawn packets,
/// ZC_EL_INIT, HP, the HP meter, SP, and the master's speed (status_calc_bl SCB_SPEED).
pub fn load_end_ack(c: &Ctx, aid: u32) {
    let Some(ed) = of_master(c, aid) else { return };
    let Some(mut u) = unit::get(c, ed.id) else { return };
    if !unit::addblock(c, &mut u) {
        return;
    }
    crate::clif::spawn(c, &u, false);
    clif::clif_elemental_info(c, aid);
    clif::clif_elemental_updatestatus(c, aid, SP_HP);
    if let Some(ed) = get(c, ed.id) {
        clif::clif_hpmeter_single(c, aid, ed.id, ed.battle_status.hp, ed.battle_status.max_hp);
    }
    clif::clif_elemental_updatestatus(c, aid, SP_SP);
    crate::status::calc_bl(c, ed.id, 1u64 << crate::status::consts::SCB_SPEED, crate::status::SCO_NONE); //Elemental mimic their master's speed on each map change
}

use data::{elemental_ai_state, elemental_data};
use spacetimedb::Table;
use crate::unit::unit as _;
