//! getunitdata / setunitdata on monsters (the BL_MOB cases of script.cpp buildin_getunitdata / buildin_setunitdata),
//! over the mob agent's mob_data (crate::mob): md->status / md->base_status, md->level, md->special_state.ai, …
//! Boot scripts use them right after `monster` (OnInit: setunitdata $@mobid[0], UMOB_*).

use crate::mob::{self, MobData};
use crate::pc_equip::*;
use crate::script::buildins::*;
use crate::script::vm::*;
use crate::status::data::StatusData;
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// A script constant (UMOB_*) by name.
fn k(name: &str) -> i32 {
    crate::data::constant(name).map(|v| v as i32).unwrap_or(i32::MIN)
}

/// The BL_MOB block of buildin_getunitdata: every UMOB_* value into array[UMOB_*].
#[allow(clippy::too_many_arguments)]
pub fn getunitdata_mob(rt: &Rt, st: &mut State, u: &Unit, sd: Option<u32>, id: i32, name: &[u8], rf: &RegRef) {
    let c = rt.c.unwrap();
    let Some(md) = mob::get(c, u.id) else { return };
    let s = &md.status;
    let vd = &u.vd;
    let ud = &u.ud;
    let vals: Vec<(&str, i64)> = vec![
        ("UMOB_SIZE", s.size as i64),
        ("UMOB_LEVEL", md.level as i64),
        ("UMOB_HP", s.hp as i64),
        ("UMOB_MAXHP", s.max_hp as i64),
        ("UMOB_MASTERAID", md.master_id as i64),
        ("UMOB_MAPID", u.map as i64),
        ("UMOB_X", u.x as i64),
        ("UMOB_Y", u.y as i64),
        ("UMOB_SPEED", s.speed as i64),
        ("UMOB_MODE", s.mode as i64),
        ("UMOB_AI", md.special_ai as i64),
        ("UMOB_SCOPTION", u.sc.option as i64),
        ("UMOB_SEX", vd.sex as i8 as i64),
        ("UMOB_CLASS", vd.class as i64),
        ("UMOB_HAIRSTYLE", vd.hair as i64),
        ("UMOB_HAIRCOLOR", vd.hair_color as i64),
        ("UMOB_HEADBOTTOM", vd.head_bottom as i64),
        ("UMOB_HEADMIDDLE", vd.head_mid as i64),
        ("UMOB_HEADTOP", vd.head_top as i64),
        ("UMOB_CLOTHCOLOR", vd.clothes_color as i64),
        ("UMOB_SHIELD", vd.shield as i64),
        ("UMOB_WEAPON", vd.weapon as i64),
        ("UMOB_LOOKDIR", ud.dir as i64),
        ("UMOB_CANMOVETICK", ud.canmove_tick),
        ("UMOB_STR", s.str as i64),
        ("UMOB_AGI", s.agi as i64),
        ("UMOB_VIT", s.vit as i64),
        ("UMOB_INT", s.int_ as i64),
        ("UMOB_DEX", s.dex as i64),
        ("UMOB_LUK", s.luk as i64),
        ("UMOB_SLAVECPYMSTRMD", md.copy_master_mode as i64),
        ("UMOB_DMGIMMUNE", ud.immune_attack as i64),
        ("UMOB_ATKRANGE", s.rhw.range as i64),
        ("UMOB_ATKMIN", s.rhw.atk as i64),
        ("UMOB_ATKMAX", s.rhw.atk2 as i64),
        ("UMOB_MATKMIN", s.matk_min as i64),
        ("UMOB_MATKMAX", s.matk_max as i64),
        ("UMOB_DEF", s.def as i64),
        ("UMOB_MDEF", s.mdef as i64),
        ("UMOB_HIT", s.hit as i64),
        ("UMOB_FLEE", s.flee as i64),
        ("UMOB_PDODGE", s.flee2 as i64),
        ("UMOB_CRIT", s.cri as i64),
        ("UMOB_RACE", s.race as i64),
        ("UMOB_ELETYPE", s.def_ele as i64),
        ("UMOB_ELELEVEL", s.ele_lv as i64),
        ("UMOB_AMOTION", s.amotion as i64),
        ("UMOB_ADELAY", s.adelay as i64),
        ("UMOB_DMOTION", s.dmotion as i64),
        ("UMOB_TARGETID", md.target_id as i64),
        ("UMOB_ROBE", vd.robe as i64),
        ("UMOB_BODY2", vd.body2 as i64),
        ("UMOB_GROUP_ID", ud.group_id as i64),
        ("UMOB_IGNORE_CELL_STACK_LIMIT", ud.ignore_cell_stack_limit as i64),
        ("UMOB_RES", s.res as i64),
        ("UMOB_MRES", s.mres as i64),
        ("UMOB_DAMAGETAKEN", md.damagetaken as i64),
    ];
    for (key, v) in vals {
        crate::script::reg::set_reg_num(rt, st, sd, id, k(key) as u32, name, v, rf);
    }
}

/// status.hpp status_db.getSCB_BATTLE(): every SCB_* but SCB_BASE and SCB_DYE.
fn scb_battle() -> u64 {
    use crate::status::consts::{SCB_BASE, SCB_DYE};
    u64::MAX & !(1u64 << SCB_BASE) & !(1u64 << SCB_DYE)
}

/// mob.cpp mob_set_dynamic_viewdata: the monster gets its own copy of its view data (md->vd_changed); the module's
/// view data is per unit already, so only the flag changes (mob_spawn frees the copy).
fn mob_set_dynamic_viewdata(md: &mut MobData) {
    if !md.vd_changed {
        md.vd_changed = true;
    }
}

/// clif.cpp clif_changelook for a monster (PACKETVER >= 4): the look into its view data, then clif_sprite_change to
/// the area (SELF while GM-hidden). The *_ignorepalette battle flags are all off.
fn changelook_mob(c: &Ctx, id: u32, look: u8, val: i32) {
    let Some(mut u) = unit::get(c, id) else { return };
    let (mut t, mut val, mut val2) = (look, val, 0);
    match look {
        LOOK_WEAPON => u.vd.weapon = val,
        LOOK_SHIELD => u.vd.shield = val,
        LOOK_HAIR => u.vd.hair = val,
        LOOK_HEAD_BOTTOM => u.vd.head_bottom = val,
        LOOK_HEAD_TOP => u.vd.head_top = val,
        LOOK_HEAD_MID => u.vd.head_mid = val,
        LOOK_HAIR_COLOR => u.vd.hair_color = val,
        LOOK_CLOTHES_COLOR => u.vd.clothes_color = val,
        LOOK_ROBE => u.vd.robe = val,
        LOOK_BODY2 => u.vd.body2 = val,
        _ => {}
    }
    unit::save(c, &u);
    // prevent leaking the presence of GM-hidden objects
    let target = if u.sc.option & crate::status::defs::OPTION_INVISIBLE != 0 { crate::clif::Target::SelfOnly } else { crate::clif::Target::Area };
    if t == LOOK_WEAPON || t == LOOK_SHIELD {
        t = LOOK_WEAPON;
        val = u.vd.weapon;
        val2 = u.vd.shield;
    }
    crate::clif::sprite_change(c, &u, u.id, t, val, val2, target);
}

/// md->base_status (allocated at the start of setunitdata).
fn base(md: &mut MobData) -> &mut StatusData {
    md.base_status.get_or_insert_with(StatusData::default)
}

/// clif_name_area(md) with the monster's current data.
fn name_area(c: &Ctx, id: u32) {
    if let Some(u) = unit::get(c, id) {
        crate::clif::name_area(c, &u);
    }
}

/// The BL_MOB block of buildin_setunitdata. `mapname` is set for UMOB_MAPID given as a map name.
pub fn setunitdata_mob(c: &Ctx, id: u32, t: i32, value: i32, mapname: Option<&[u8]>) -> i32 {
    let Some(mut md) = mob::get(c, id) else { return SCRIPT_CMD_FAILURE };
    if md.base_status.is_none() {
        // md->base_status = calloc + memcpy(md->db->status)
        md.base_status = Some(mob::db(c, &md).map(|d| crate::mob::status::status_from_db(&d.status)).unwrap_or_default());
    }
    // Check if the view data will be modified (UMOB_CLASS: status_set_viewdata does it)
    let dynamic = ["UMOB_SEX", "UMOB_HAIRSTYLE", "UMOB_HAIRCOLOR", "UMOB_HEADBOTTOM", "UMOB_HEADMIDDLE", "UMOB_HEADTOP", "UMOB_CLOTHCOLOR", "UMOB_SHIELD", "UMOB_WEAPON", "UMOB_ROBE", "UMOB_BODY2"];
    if dynamic.iter().any(|n| k(n) == t) {
        mob_set_dynamic_viewdata(&mut md);
    }
    let mut calc_status = false;
    let lvl = md.level;
    let looks = [
        ("UMOB_HAIRSTYLE", LOOK_HAIR),
        ("UMOB_HAIRCOLOR", LOOK_HAIR_COLOR),
        ("UMOB_HEADBOTTOM", LOOK_HEAD_BOTTOM),
        ("UMOB_HEADMIDDLE", LOOK_HEAD_MID),
        ("UMOB_HEADTOP", LOOK_HEAD_TOP),
        ("UMOB_CLOTHCOLOR", LOOK_CLOTHES_COLOR),
        ("UMOB_SHIELD", LOOK_SHIELD),
        ("UMOB_WEAPON", LOOK_WEAPON),
        ("UMOB_ROBE", LOOK_ROBE),
        ("UMOB_BODY2", LOOK_BODY2),
    ];
    // the stats: base value, then status_calc_misc on the battle status (md->status) and a battle recalculation
    let stats: [(&str, fn(&mut StatusData) -> &mut i16); 6] = [
        ("UMOB_STR", |s| &mut s.str),
        ("UMOB_AGI", |s| &mut s.agi),
        ("UMOB_VIT", |s| &mut s.vit),
        ("UMOB_INT", |s| &mut s.int_),
        ("UMOB_DEX", |s| &mut s.dex),
        ("UMOB_LUK", |s| &mut s.luk),
    ];
    if t == k("UMOB_SIZE") {
        md.status.size = value as u8;
        base(&mut md).size = value as u8;
        mob::put(c, &md);
    } else if t == k("UMOB_LEVEL") {
        md.level = value as u16 as i32;
        mob::put(c, &md);
        name_area(c, id);
    } else if t == k("UMOB_HP") {
        base(&mut md).hp = value as u32;
        mob::put(c, &md);
        crate::status::hp::status_set_hp(c, id, value as u32, 0);
        name_area(c, id);
    } else if t == k("UMOB_MAXHP") {
        base(&mut md).hp = value as u32;
        base(&mut md).max_hp = value as u32;
        mob::put(c, &md);
        crate::status::hp::status_set_maxhp(c, id, value as u32, 0);
        name_area(c, id);
    } else if t == k("UMOB_MASTERAID") {
        md.master_id = value as u32;
        mob::put(c, &md);
    } else if t == k("UMOB_MAPID") {
        mob::put(c, &md);
        let m = match mapname {
            Some(n) => crate::data::maps().id_of(&lat(n)).map(|m| m as i32).unwrap_or(-1),
            None => value,
        };
        crate::mob::unit::unit_warp(c, id, m as i16 as i32, 0, 0, unit::CLR_TELEPORT);
    } else if t == k("UMOB_X") || t == k("UMOB_Y") {
        mob::put(c, &md);
        let Some(u) = unit::get(c, id) else { return SCRIPT_CMD_SUCCESS };
        let (x, y) = if t == k("UMOB_X") { (value as i16, u.y) } else { (u.x, value as i16) };
        if !unit::walktoxy(c, id, x, y, 2) {
            unit::movepos(c, id, x, y, 0, false);
        }
    } else if t == k("UMOB_SPEED") {
        base(&mut md).speed = value as u16;
        crate::mob::status::status_calc_misc_mob(&mut md.status, lvl);
        calc_status = true;
        mob::put(c, &md);
    } else if t == k("UMOB_MODE") {
        base(&mut md).mode = value;
        mob::put(c, &md);
        // Mob mode must be updated before calling unit_refresh
        crate::mob::status_bl::status_calc_bl_mob(c, id, scb_battle(), crate::status::defs::SCO_NONE);
        if let Some(u) = unit::get(c, id) {
            crate::unit_npc::unit_refresh(c, &u, false);
        }
    } else if t == k("UMOB_AI") {
        md.special_ai = value as u8;
        mob::put(c, &md);
    } else if t == k("UMOB_SCOPTION") {
        mob::put(c, &md);
        if let Some(mut u) = unit::get(c, id) {
            u.sc.option = value as u16 as i32;
            unit::save(c, &u);
        }
    } else if t == k("UMOB_SEX") {
        mob::put(c, &md);
        if let Some(mut u) = unit::get(c, id) {
            u.vd.sex = value as u8;
            unit::save(c, &u);
            crate::unit_npc::unit_refresh(c, &u, false);
        }
    } else if t == k("UMOB_CLASS") {
        crate::mob::status::status_set_viewdata(c, &mut md, value as u16 as i32);
        mob::put(c, &md);
        if let Some(u) = unit::get(c, id) {
            crate::unit_npc::unit_refresh(c, &u, false);
        }
    } else if let Some(look) = looks.iter().find(|(n, _)| k(n) == t).map(|(_, l)| *l) {
        mob::put(c, &md);
        changelook_mob(c, id, look, value as u16 as i32);
    } else if t == k("UMOB_LOOKDIR") {
        mob::put(c, &md);
        if let Some(mut u) = unit::get(c, id) {
            unit::setdir(c, &mut u, value as u8, true);
            unit::save(c, &u);
        }
    } else if t == k("UMOB_CANMOVETICK") {
        mob::put(c, &md);
        if let Some(mut u) = unit::get(c, id) {
            u.ud.canmove_tick = if value > 0 { value as u32 as i64 } else { 0 };
            unit::save(c, &u);
        }
    } else if let Some(field) = stats.iter().find(|(n, _)| k(n) == t).map(|(_, f)| *f) {
        *field(base(&mut md)) = value as u16 as i16;
        crate::mob::status::status_calc_misc_mob(&mut md.status, lvl);
        calc_status = true;
        mob::put(c, &md);
    } else if t == k("UMOB_SLAVECPYMSTRMD") {
        if value > 0 {
            let master_mode = if md.master_id != 0 { mob::get(c, md.master_id).map(|m2| m2.status.mode) } else { None };
            match master_mode {
                None => {
                    log::warn!("buildin_setunitdata: Trying to set UMOB_SLAVECPYMSTRMD on mob without master!");
                    mob::put(c, &md);
                    return SCRIPT_CMD_SUCCESS; // break (no status calculation)
                }
                Some(mode) => {
                    base(&mut md).mode = mode;
                    md.copy_master_mode = true;
                }
            }
        } else {
            md.copy_master_mode = false;
        }
        calc_status = true;
        mob::put(c, &md);
    } else if t == k("UMOB_DMGIMMUNE") {
        mob::put(c, &md);
        if let Some(mut u) = unit::get(c, id) {
            u.ud.immune_attack = value > 0;
            unit::save(c, &u);
        }
    } else if t == k("UMOB_TARGETID") {
        mob::put(c, &md);
        if value == 0 {
            crate::mob::ai::mob_unlocktarget(c, id, c.tick());
        } else {
            let Some(target) = unit::get(c, value as u32) else {
                // map_id2bl: a floating NPC has no unit row, and cannot be a target either
                log::warn!("buildin_setunitdata: Error in finding target for BL_MOB!");
                return SCRIPT_CMD_FAILURE;
            };
            crate::mob::ai::mob_target(c, id, &target, 0);
        }
    } else if t == k("UMOB_GROUP_ID") {
        mob::put(c, &md);
        if let Some(mut u) = unit::get(c, id) {
            u.ud.group_id = value;
            unit::save(c, &u);
            crate::unit_npc::unit_refresh(c, &u, false);
        }
    } else if t == k("UMOB_IGNORE_CELL_STACK_LIMIT") {
        mob::put(c, &md);
        if let Some(mut u) = unit::get(c, id) {
            u.ud.ignore_cell_stack_limit = value > 0;
            unit::save(c, &u);
        }
    } else if t == k("UMOB_RACE") {
        md.status.race = value as u8;
        base(&mut md).race = value as u8;
        mob::put(c, &md);
    } else if t == k("UMOB_DAMAGETAKEN") {
        md.damagetaken = value as u16;
        mob::put(c, &md);
    } else {
        // the base-status values followed by a battle recalculation
        let b = base(&mut md);
        if t == k("UMOB_ATKRANGE") {
            b.rhw.range = value as u16;
        } else if t == k("UMOB_ATKMIN") {
            b.rhw.atk = value as u16;
        } else if t == k("UMOB_ATKMAX") {
            b.rhw.atk2 = value as u16;
        } else if t == k("UMOB_MATKMIN") {
            b.matk_min = value as u16;
        } else if t == k("UMOB_MATKMAX") {
            b.matk_max = value as u16;
        } else if t == k("UMOB_DEF") {
            b.def = value as i16;
        } else if t == k("UMOB_MDEF") {
            b.mdef = value as i16;
        } else if t == k("UMOB_HIT") {
            b.hit = value as i16;
        } else if t == k("UMOB_FLEE") {
            b.flee = value as i16;
        } else if t == k("UMOB_PDODGE") {
            b.flee2 = value as i16;
        } else if t == k("UMOB_CRIT") {
            b.cri = value as i16;
        } else if t == k("UMOB_ELETYPE") {
            b.def_ele = value as u8;
        } else if t == k("UMOB_ELELEVEL") {
            b.ele_lv = value as u8;
        } else if t == k("UMOB_AMOTION") {
            b.amotion = value as i16 as u16;
        } else if t == k("UMOB_ADELAY") {
            b.adelay = value as i16 as u16;
        } else if t == k("UMOB_DMOTION") {
            b.dmotion = value as i16 as u16;
        } else if t == k("UMOB_RES") {
            b.res = value as i16;
        } else if t == k("UMOB_MRES") {
            b.mres = value as i16;
        } else {
            // (md->base_status stays allocated, as in rAthena)
            mob::put(c, &md);
            log::error!("buildin_setunitdata: Unknown data identifier {t} for BL_MOB.");
            return SCRIPT_CMD_FAILURE;
        }
        calc_status = true;
        mob::put(c, &md);
    }
    if calc_status {
        crate::mob::status_bl::status_calc_bl_mob(c, id, scb_battle(), crate::status::defs::SCO_NONE);
    }
    SCRIPT_CMD_SUCCESS
}
