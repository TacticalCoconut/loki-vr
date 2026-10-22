//! map/homunculus.cpp (rAthena e985006, RENEWAL): homunculi. A homunculus is created by the char server
//! (hom_create_request → intif_homunculus_create → int_homun.rs → hom_recv_data), loaded at login or by Call Homunculus
//! (intif_homunculus_requestload → hom_recv_data → hom_alloc), and then is a unit of its own (BL_HOM) with its
//! `hom_data` row (`data`): called, vaporized, resurrected, fed, loved, levelled, evolved, mutated and saved here. The
//! client drives it (move / attack / skill requests: `clif`); homunculi have no server-side AI in rAthena.
//! Submodules:
//! - `data`: homun_data as a table, the homunculus.hpp enums and constants.
//! - `clif`: the homunculus packets (clif_hominfo, clif_send_homdata, clif_homskillinfoblock, clif_homskillup,
//!   clif_hom_food, clif_homunculus_updatestatus) and the request parsers (clif_parse_Hom*, ChangeHomunculusName).
//! - `status`: status_calc_homunculus_ and the BL_HOM parts of status.cpp.
//! - `hp`: status_damage / status_heal / status_revive and status_natural_heal for BL_HOM (status.cpp).
//! - `unit`: the unit.cpp parts for BL_HOM (unit_remove_map / unit_free / unit_warp, the master's teleport timer).
//! - `cooldown`: skill.cpp skill_blockhomun_start / end / clear (hd->scd).
//! - `buildins`: the homunculus script commands and the BL_HOM arms of the unit script commands.
//! - `cmd`: the homunculus @commands and the homunculus parts of other @commands.
//! Interfaces for other agents: [`hom_call`], [`hom_vaporize`], [`hom_ressurect`], [`data::is_active_of`],
//! [`hom_checkskill`], [`data::of_master`].

pub mod buildins;
pub mod clif;
pub mod cmd;
pub mod cooldown;
pub mod data;
pub mod hp;
pub mod status;
pub mod unit;

use crate::db::misc::homunculus::{homunculus_db, HomStats, HomunculusDb, HomunSkillTreeEntry, HM_CLASS_BASE};
use crate::int_homun::{self, HSkill, SHomunculus, HM_SKILLBASE, MAX_HOMUNSKILL, MAX_SKILLCOOLDOWN};
use crate::status::defs::{SCO_FIRST, SCO_NONE};
use crate::timer::{self, Func};
use crate::world::{Ctx, Tick};
use clif::*;
use data::*;
use crate::timer::timer as _;
use crate::unit::unit as _;
use spacetimedb::Table;

// ---------------------------------------------------------------------------------------------- configuration

/// conf/battle/homunc.conf `hom_setting: 0x3D`.
pub fn hom_setting() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::HOM_SETTING)
}
/// conf/battle/homunc.conf `homunculus_friendly_rate: 100`.
pub fn homunculus_friendly_rate() -> u32 {
    crate::battle_config::get(crate::battle_config::Bc::HOMUNCULUS_FRIENDLY_RATE) as u32
}
/// conf/battle/homunc.conf `hom_rename: no`.
pub fn hom_rename() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::HOM_RENAME) != 0
}
/// conf/battle/homunc.conf `homunculus_evo_intimacy_need: 91100`, `homunculus_evo_intimacy_reset: 1000`.
pub fn homunculus_evo_intimacy_need() -> u32 {
    crate::battle_config::get(crate::battle_config::Bc::HOMUNCULUS_EVO_INTIMACY_NEED) as u32
}
pub fn homunculus_evo_intimacy_reset() -> u32 {
    crate::battle_config::get(crate::battle_config::Bc::HOMUNCULUS_EVO_INTIMACY_RESET) as u32
}
/// conf/battle/homunc.conf `hvan_explosion_intimate: 45000`.
pub fn hvan_explosion_intimate() -> u32 {
    crate::battle_config::get(crate::battle_config::Bc::HVAN_EXPLOSION_INTIMATE) as u32
}
/// conf/battle/homunc.conf `homunculus_show_growth: yes`.
pub fn homunculus_show_growth() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::HOMUNCULUS_SHOW_GROWTH) != 0
}
/// conf/battle/homunc.conf `homunculus_auto_vapor: 80` (battle/conf.rs).
pub fn homunculus_auto_vapor() -> i32 {
    crate::battle::conf::homunculus_auto_vapor()
}
/// conf/battle/homunc.conf `homunculus_max_level: 99`, `homunculus_S_max_level: 275`, `homunculus_S_growth_level: 99`.
pub fn hom_max_level() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::HOMUNCULUS_MAX_LEVEL)
}
pub const HOM_S_MAX_LEVEL: i32 = 275;
pub const HOM_S_GROWTH_LEVEL: i32 = 99;
/// conf/battle/homunc.conf `homunculus_autofeed_always: yes`.
pub fn homunculus_autofeed_always() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::HOMUNCULUS_AUTOFEED_ALWAYS) != 0
}
/// conf/battle/homunc.conf `homunculus_exp_gain: 10` (mob/conf.rs).
pub fn homunculus_exp_gain() -> i32 {
    crate::mob::conf::homunculus_exp_gain()
}
/// conf/battle/homunc.conf `homunculus_starving_rate: 10`, `homunculus_starving_delay: 20000`.
pub fn homunculus_starving_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::HOMUNCULUS_STARVING_RATE)
}
pub fn homunculus_starving_delay() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::HOMUNCULUS_STARVING_DELAY)
}
/// conf/battle/homunc.conf `hom_idle_no_share: no` (mob/conf.rs), `idletime_hom_option: 0x1F`.
pub const HOM_IDLE_NO_SHARE: i32 = crate::mob::conf::HOM_IDLE_NO_SHARE;
pub fn idletime_hom_option() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::IDLETIME_HOM_OPTION)
}
/// battle.cpp RENEWAL defaults (homunc.conf leaves them commented): `hom_delay_reset_vaporize: 0`,
/// `hom_delay_reset_warp: 0`.
pub fn hom_delay_reset_vaporize() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::HOM_DELAY_RESET_VAPORIZE) != 0
}
pub fn hom_delay_reset_warp() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::HOM_DELAY_RESET_WARP) != 0
}
/// conf/battle/feature.conf `feature.homunculus_autofeed: on`, `feature.homunculus_autofeed_rate: 30`.
pub fn feature_homunculus_autofeed() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::FEATURE_HOMUNCULUS_AUTOFEED) != 0
}
pub fn feature_homunculus_autofeed_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FEATURE_HOMUNCULUS_AUTOFEED_RATE)
}
/// mmo.hpp MAX_SPIRITBALL.
pub const MAX_SPIRITBALL: i32 = 15;
/// skill.hpp e_skill_flag SKILL_FLAG_PERMANENT.
pub const SKILL_FLAG_PERMANENT: u8 = 0;

/// clif.hpp emotion_type values homunculus.cpp uses.
pub const ET_DELIGHT: u8 = 2;
pub const ET_FRET: u8 = 6;
pub const ET_THINK: u8 = 9;
pub const ET_KEK: u8 = 16;
pub const ET_PROFUSELY_SWEAT: u8 = 19;
pub const ET_SCRATCH: u8 = 20;
pub const ET_OK: u8 = 33;

/// homunculus.cpp intimacy_grades (min_value of each e_homun_grade).
const INTIMACY_GRADES: [u32; 7] = [100, 400, 1100, 10100, 25100, 75100, 91100];

// ---------------------------------------------------------------------------------------------- small helpers

/// map_id2sd: the player with this account id, in the world.
fn sd_of(c: &Ctx, aid: u32) -> Option<crate::pc::Pc> {
    crate::pc::get(c, aid)
}

/// sd->status.hom_id.
pub fn hom_id_of(c: &Ctx, aid: u32) -> u32 {
    crate::pc::character_of(c, aid).map(|ch| ch.homun_id).unwrap_or(0)
}

/// sd->status.hom_id = v.
pub fn set_hom_id(c: &Ctx, aid: u32, v: u32) {
    if let Some(mut ch) = crate::pc::character_of(c, aid) {
        if ch.homun_id != v {
            ch.homun_id = v;
            crate::status::put_char(c, &ch);
        }
    }
}

/// hd->master != nullptr: the master is in the world (and unit_free has not cut the link).
fn has_master(c: &Ctx, hd: &HomData) -> bool {
    master_sd(c, hd).is_some()
}

/// status_isdead(*hd).
pub fn hom_isdead(hd: &HomData) -> bool {
    hd.battle_status.hp == 0
}

// ---------------------------------------------------------------------------------------------- class / skill helpers

/// homunculus.cpp hom_skill_get_index: the index of a homunculus skill in s_homunculus::hskill, -1 when invalid.
pub fn hom_skill_get_index(skill_id: u16) -> i16 {
    if !crate::db::skills::skill_chk_homun(skill_id) {
        return -1;
    }
    let idx = skill_id - HM_SKILLBASE;
    if idx as usize >= MAX_HOMUNSKILL {
        return -1;
    }
    idx as i16
}

/// homunculus.cpp hom_class2mapid.
pub fn hom_class2mapid(hom_class: i32) -> i32 {
    match hom_class {
        // Normal Homunculus
        6001 | 6005 => MAPID_LIF,
        6002 | 6006 => MAPID_AMISTR,
        6003 | 6007 => MAPID_FILIR,
        6004 | 6008 => MAPID_VANILMIRTH,
        // Evolved Homunculus
        6009 | 6013 => MAPID_LIF_E,
        6010 | 6014 => MAPID_AMISTR_E,
        6011 | 6015 => MAPID_FILIR_E,
        6012 | 6016 => MAPID_VANILMIRTH_E,
        // Homunculus S
        6048 => MAPID_EIRA,
        6049 => MAPID_BAYERI,
        6050 => MAPID_SERA,
        6051 => MAPID_DIETER,
        6052 => MAPID_ELANOR,
        _ => -1,
    }
}

/// homunculus.cpp hom_class2type.
pub fn hom_class2type(class_: i32) -> i32 {
    let mid = hom_class2mapid(class_);
    if (mid & (HOM_REG | HOM_EVO)) == (HOM_REG | HOM_EVO) {
        HT_EVO
    } else if mid & HOM_REG != 0 {
        HT_REG
    } else if mid & HOM_S != 0 {
        HT_S
    } else {
        HT_INVALID
    }
}

/// homunculus.cpp hom_get_viewdata: hom_viewdb[class_ - HM_CLASS_BASE] (look[LOOK_BASE] = the class, nothing else).
pub fn hom_get_viewdata(class_: i32) -> Option<crate::unit::ViewData> {
    if crate::db::misc::homunculus::homdb_checkid(class_) {
        return Some(crate::unit::ViewData { class: class_, ..Default::default() });
    }
    None
}

/// homunculus.cpp hom_checkskill: the level of a learnt homunculus skill (0 when invalid or unlearnt).
pub fn hom_checkskill(hd: Option<&HomData>, skill_id: u16) -> i16 {
    let idx = hom_skill_get_index(skill_id);
    if idx < 0 {
        return 0; // Invalid skill
    }
    let Some(hd) = hd else { return 0 };
    let s = hd.homunculus.hskill.get(idx as usize).copied().unwrap_or_default();
    if s.id == skill_id {
        return s.lv as i16;
    }
    0
}

/// `hom_checkskill(map_id2hd(id), skill_id)`.
pub fn hom_checkskill_id(c: &Ctx, hd_id: u32, skill_id: u16) -> i16 {
    hom_checkskill(get(c, hd_id).as_ref(), skill_id)
}

/// homunculus.cpp hom_skill_tree_get_max.
pub fn hom_skill_tree_get_max(skill_id: u16, b_class: i32) -> i32 {
    let Some(homun) = homunculus_db().homun_search(b_class) else { return 0 };
    homun.skill_tree.iter().find(|s| s.id == skill_id).map(|s| s.max as i32).unwrap_or(0)
}

/// homunculus.cpp hom_skill_get_min_level.
pub fn hom_skill_get_min_level(class_: i32, skill_id: u16) -> u16 {
    let Some(homun) = homunculus_db().homun_search(class_) else { return 0 };
    homun.skill_tree.iter().find(|s| s.id == skill_id).map(|s| s.need_level).unwrap_or(0)
}

/// homunculus.cpp hom_intimacy_grade2intimacy.
pub fn hom_intimacy_grade2intimacy(grade: u8) -> u32 {
    if grade > HOMGRADE_LOYAL {
        return 0;
    }
    INTIMACY_GRADES[grade as usize]
}

/// homunculus.cpp hom_intimacy_intimacy2grade.
pub fn hom_intimacy_intimacy2grade(intimacy: u32) -> u8 {
    for g in [HOMGRADE_LOYAL, HOMGRADE_CORDIAL, HOMGRADE_NEUTRAL, HOMGRADE_SHY, HOMGRADE_AWKWARD, HOMGRADE_HATE] {
        if intimacy >= INTIMACY_GRADES[g as usize] {
            return g;
        }
    }
    HOMGRADE_HATE_WITH_PASSION
}

/// homunculus.cpp hom_get_intimacy_grade.
pub fn hom_get_intimacy_grade(hd: &HomData) -> u8 {
    hom_intimacy_intimacy2grade(hd.homunculus.intimacy)
}

// ---------------------------------------------------------------------------------------------- spirit spheres

/// homunculus.cpp hom_addspiritball.
pub fn hom_addspiritball(c: &Ctx, hd_id: u32, max: i32) {
    let Some(mut hd) = get(c, hd_id) else { return };
    let max = max.min(MAX_SPIRITBALL);
    if hd.homunculus.spiritball < 0 {
        hd.homunculus.spiritball = 0;
    }
    if hd.homunculus.spiritball != 0 && hd.homunculus.spiritball as i32 >= max {
        hd.homunculus.spiritball = max as i8;
    } else {
        hd.homunculus.spiritball = hd.homunculus.spiritball.wrapping_add(1);
    }
    put(c, &hd);
    if let Some(u) = crate::unit::get(c, hd_id) {
        crate::skill::clif::clif_spiritball(c, &u, None, crate::clif::Target::Area);
    }
}

/// homunculus.cpp hom_delspiritball (type 1: no client update).
pub fn hom_delspiritball(c: &Ctx, hd_id: u32, count: i32, type_: i32) {
    let Some(mut hd) = get(c, hd_id) else { return };
    if hd.homunculus.spiritball <= 0 {
        hd.homunculus.spiritball = 0;
        put(c, &hd);
        return;
    }
    if count <= 0 {
        return;
    }
    let count = count.min(MAX_SPIRITBALL).min(hd.homunculus.spiritball as i32);
    hd.homunculus.spiritball -= count as i8;
    put(c, &hd);
    if type_ == 0 {
        if let Some(u) = crate::unit::get(c, hd_id) {
            crate::skill::clif::clif_spiritball(c, &u, None, crate::clif::Target::Area);
        }
    }
}

// ---------------------------------------------------------------------------------------------- death / vaporize / delete

/// homunculus.cpp hom_dead: the hunger timer stops, homunculus.hp = 0, SC_HOMUN_TIME ends on the master. Always 3
/// (standard death, removed from the map; a homunculus without intimacy is freed by unit_remove_map).
pub fn hom_dead(c: &Ctx, hd_id: u32) -> i32 {
    //There's no intimacy penalties on death (from Tharis)
    //Delete timers when dead.
    hom_hungry_timer_delete(c, hd_id);
    let Some(mut hd) = get(c, hd_id) else { return 3 };
    hd.homunculus.hp = 0;
    put(c, &hd);
    if !has_master(c, &hd) {
        //unit remove map will invoke unit free
        return 3;
    }
    // RENEWAL
    crate::status::sc::status_change_end(c, hd.master, crate::status::consts::SC_HOMUN_TIME, 0);
    //Remove from map (if it has no intimacy, it is auto-removed from memory)
    3
}

/// homunculus.cpp hom_vaporize: the homunculus goes to rest (flag 1 HOM_ST_REST: only at 80% HP or more; 2
/// HOM_ST_MORPH; 0 is taken as HOM_ST_REST without the HP check). 0 = nothing done.
pub fn hom_vaporize(c: &Ctx, aid: u32, flag: i32) -> i32 {
    if sd_of(c, aid).is_none() {
        return 0;
    }
    let Some(mut hd) = of_master(c, aid) else { return 0 };
    if hd.homunculus.vaporize != 0 {
        return 0;
    }
    if hom_isdead(&hd) {
        return 0; //Can't vaporize a dead homun.
    }
    if flag == HOM_ST_REST && crate::pet::get_percentage(hd.battle_status.hp, hd.battle_status.max_hp) < 80 {
        return 0;
    }
    hd.regen.block = 3; //Block regen while vaporized.
    put(c, &hd);
    //Delete timers when vaporized.
    hom_hungry_timer_delete(c, hd.id);
    let hd_id = hd.id;
    with(c, hd_id, |hd| hd.homunculus.vaporize = if flag != 0 { flag as i16 } else { HOM_ST_REST as i16 });
    if hom_delay_reset_vaporize() {
        cooldown::skill_blockhomun_clear(c, hd_id);
    }
    crate::status::sc::status_change_clear(c, hd_id, 1);
    clif_hominfo(c, aid, hd_id, 0);
    hom_save(c, hd_id);
    // RENEWAL
    crate::status::sc::status_change_end(c, aid, crate::status::consts::SC_HOMUN_TIME, 0);
    unit::unit_remove_map(c, hd_id, crate::unit::CLR_OUTSIGHT) as i32
}

/// homunculus.cpp hom_delete: the homunculus is "killed" for good (intimacy 0, HP 0, the dead info to the master) and
/// leaves the map (unit_remove_map frees a homunculus without intimacy). Without a master: unit_free.
pub fn hom_delete(c: &Ctx, hd_id: u32) -> i32 {
    let Some(mut hd) = get(c, hd_id) else { return 0 };
    if !has_master(c, &hd) {
        return unit::unit_free(c, hd_id, crate::unit::CLR_DEAD);
    }
    //This makes it be deleted right away.
    hd.homunculus.intimacy = 0;
    // Send homunculus_dead to client
    hd.homunculus.hp = 0;
    put(c, &hd);
    clif_hominfo(c, hd.master, hd_id, 0);
    unit::unit_remove_map(c, hd_id, crate::unit::CLR_OUTSIGHT) as i32
}

// ---------------------------------------------------------------------------------------------- skill tree

/// homunculus.cpp hom_calc_skilltree_sub: the skills of `skill_tree` the homunculus meets the requirements of (all of
/// them with skillfree) become known (hskill[idx].id).
fn hom_calc_skilltree_sub(hd: &mut HomData, skill_tree: &[HomunSkillTreeEntry]) {
    let evolved = hd.db().map(|db| hd.homunculus.class_ as i32 == db.evo_class).unwrap_or(false);
    for skit in skill_tree {
        let skill_id = skit.id;
        let idx = hom_skill_get_index(skill_id);
        if skill_id == 0 || idx == -1 {
            continue;
        }
        if hd.homunculus.hskill[idx as usize].id != 0 {
            continue; //Skill already known.
        }
        let mut fail = false;
        if !crate::skilltree::skillfree() {
            if skit.intimacy > 0 && hd.homunculus.intimacy < skit.intimacy {
                continue;
            }
            if skit.evolution && !evolved {
                continue;
            }
            if skit.need_level as i32 > hd.homunculus.level as i32 {
                continue;
            }
            for &(need_id, need_lv) in &skit.need {
                if need_id > 0 && (hom_checkskill(Some(hd), need_id) as i32) < need_lv as i32 {
                    fail = true;
                    break;
                }
            }
        }
        if !fail {
            hd.homunculus.hskill[idx as usize].id = skill_id;
        }
    }
}

/// homunculus.cpp hom_calc_skilltree without the packet (status_calc_homunculus_ runs it on the row it is building;
/// clif_homskillinfoblock follows once the row is saved).
pub fn hom_calc_skilltree_data(hd: &mut HomData) {
    let Some(homun_current) = homunculus_db().homun_search(hd.homunculus.class_ as i32) else {
        // If the current class can't be loaded, then for sure there's no prev_class!
        return;
    };
    /* load previous homunculus form skills first. */
    if let Some(homun) = homunculus_db().homun_search(hd.homunculus.prev_class as i32) {
        hom_calc_skilltree_sub(hd, &homun.skill_tree);
    }
    hom_calc_skilltree_sub(hd, &homun_current.skill_tree);
}

/// homunculus.cpp hom_calc_skilltree.
pub fn hom_calc_skilltree(c: &Ctx, hd_id: u32) {
    let Some(mut hd) = get(c, hd_id) else { return };
    if homunculus_db().homun_search(hd.homunculus.class_ as i32).is_none() {
        return;
    }
    hom_calc_skilltree_data(&mut hd);
    put(c, &hd);
    clif_homskillinfoblock(c, hd_id);
}

/// homunculus.cpp hom_skillup: a skill point goes into `skill_id` (clif_parse_SkillUp through pc_skillup).
pub fn hom_skillup(c: &Ctx, hd_id: u32, skill_id: u16) {
    let Some(mut hd) = get(c, hd_id) else { return };
    if hd.homunculus.vaporize != 0 {
        return;
    }
    let idx = hom_skill_get_index(skill_id);
    if idx < 0 {
        return;
    }
    let s = hd.homunculus.hskill[idx as usize];
    let class_ = hd.homunculus.class_ as i32;
    if hd.homunculus.skillpts > 0
        && s.id != 0
        && s.flag == SKILL_FLAG_PERMANENT //Don't allow raising while you have granted skills. [Skotlex]
        && hd.homunculus.level as i32 >= hom_skill_get_min_level(class_, skill_id) as i32
        && (s.lv as i32) < hom_skill_tree_get_max(skill_id, class_)
    {
        hd.homunculus.hskill[idx as usize].lv += 1;
        hd.homunculus.skillpts -= 1;
        put(c, &hd);
        status::status_calc_homunculus(c, hd_id, SCO_NONE);
        clif_homskillup(c, hd_id, skill_id);
        if let Some(hd) = get(c, hd_id) {
            if has_master(c, &hd) {
                clif_hominfo(c, hd.master, hd_id, 0);
            }
        }
        clif_homskillinfoblock(c, hd_id);
    }
}

// ---------------------------------------------------------------------------------------------- level / evolution

/// homunculus.cpp hom_levelup: one level (growth rolls, a skill point every 3 levels, the packets and the growth
/// message). 0 = no level up.
pub fn hom_levelup(c: &Ctx, hd_id: u32) -> i32 {
    let Some(mut hd) = get(c, hd_id) else { return 0 };
    let m_class = hom_class2mapid(hd.homunculus.class_ as i32);
    if m_class == -1 {
        log::error!("hom_levelup: Invalid class {}.", hd.homunculus.class_);
        return 0;
    }
    let mut minmax: Option<(HomStats, HomStats)> = None;
    // When homunculus is homunculus S, we check to see if we need to apply previous class stats
    if m_class & HOM_S != 0 && (hd.homunculus.level as i32) < HOM_S_GROWTH_LEVEL {
        let Some(homun_s_db) = homunculus_db().homun_search(hd.homunculus.prev_class as i32) else {
            log::error!("hom_levelup: Failed to find database entry for {}.", hd.homunculus.prev_class);
            return 0;
        };
        minmax = Some((homun_s_db.gmin, homun_s_db.gmax));
    }
    if (m_class & HOM_REG != 0 && hd.homunculus.level as i32 >= hom_max_level()) || (m_class & HOM_S != 0 && hd.homunculus.level as i32 >= HOM_S_MAX_LEVEL) || hd.exp_next == 0 || hd.homunculus.exp < hd.exp_next {
        return 0;
    }
    let hom = &mut hd.homunculus;
    hom.level += 1;
    if hom.level % 3 == 0 {
        hom.skillpts += 1; //1 skillpoint each 3 base level
    }
    hom.exp -= hd.exp_next;
    hd.exp_next = homunculus_db().get_nextexp(hd.homunculus.level as u16);
    let (min, max) = match minmax {
        Some(m) => m,
        None => match hd.db() {
            Some(db) => (db.gmin, db.gmax),
            None => return 0,
        },
    };
    let growth_max_hp = c.rnd_value(min.hp as i32, max.hp as i32);
    let growth_max_sp = c.rnd_value(min.sp as i32, max.sp as i32);
    let mut growth_str = c.rnd_value(min.str as i32, max.str as i32);
    let mut growth_agi = c.rnd_value(min.agi as i32, max.agi as i32);
    let mut growth_vit = c.rnd_value(min.vit as i32, max.vit as i32);
    let mut growth_dex = c.rnd_value(min.dex as i32, max.dex as i32);
    let mut growth_int = c.rnd_value(min.int_ as i32, max.int_ as i32);
    let mut growth_luk = c.rnd_value(min.luk as i32, max.luk as i32);
    //Aegis discards the decimals in the stat growth values!
    growth_str -= growth_str % 10;
    growth_agi -= growth_agi % 10;
    growth_vit -= growth_vit % 10;
    growth_dex -= growth_dex % 10;
    growth_int -= growth_int % 10;
    growth_luk -= growth_luk % 10;
    let hom = &mut hd.homunculus;
    hom.max_hp = hom.max_hp.wrapping_add(growth_max_hp as u32);
    hom.max_sp = hom.max_sp.wrapping_add(growth_max_sp as u32);
    hom.str += growth_str;
    hom.agi += growth_agi;
    hom.vit += growth_vit;
    hom.dex += growth_dex;
    hom.int_ += growth_int;
    hom.luk += growth_luk;
    apply_homun_level_statweight(hom);
    put(c, &hd);
    // Needed to update skill list for mutated homunculus so unlocked skills will appear when the needed level is reached.
    status::status_calc_homunculus(c, hd_id, SCO_NONE);
    let master = hd.master;
    clif_hominfo(c, master, hd_id, 0);
    clif_homunculus_updatestatus(c, master, crate::status::consts::SP_BASEEXP);
    clif_homskillinfoblock(c, hd_id);
    if homunculus_show_growth() && sd_of(c, master).is_some() {
        let output = format!(
            "Growth: hp:{} sp:{} str({:.2}) agi({:.2}) vit({:.2}) int({:.2}) dex({:.2}) luk({:.2}) ",
            growth_max_hp,
            growth_max_sp,
            growth_str as f64 / 10.0,
            growth_agi as f64 / 10.0,
            growth_vit as f64 / 10.0,
            growth_int as f64 / 10.0,
            growth_dex as f64 / 10.0,
            growth_luk as f64 / 10.0
        );
        if let Some(mu) = crate::unit::get(c, master) {
            crate::clif::messagecolor_target(c, &mu, crate::atcmd_pc::COLOR_LIGHT_GREEN, &crate::net::wire::latin1(&output), false, crate::clif::Target::SelfOnly, None);
        }
    }
    1
}

/// homunculus.hpp APPLY_HOMUN_LEVEL_STATWEIGHT (RENEWAL, HOMUN_LEVEL_STATWEIGHT_VALUE 0): every *_value = level / 10.
pub fn apply_homun_level_statweight(hom: &mut SHomunculus) {
    let v = hom.level as i32 / 10;
    hom.str_value = v;
    hom.agi_value = v;
    hom.vit_value = v;
    hom.int_value = v;
    hom.dex_value = v;
    hom.luk_value = v;
}

/// homunculus.cpp hom_change_class: the database entry and the class change (and the view data). False when the
/// class has no database entry.
fn hom_change_class(c: &Ctx, hd_id: u32, class_: i32) -> bool {
    let Some(homun) = homunculus_db().homun_search(class_) else { return false };
    let Some(mut hd) = get(c, hd_id) else { return false };
    hd.db_class = homun.base_class;
    hd.homunculus.class_ = class_ as i16;
    put(c, &hd);
    status::status_set_viewdata(c, hd_id, class_);
    true
}

/// The evolution bonuses (hom_evolution / hom_shuffle): emin..emax HP / SP and 10× the stats.
fn apply_evolution_bonus(c: &Ctx, hom: &mut SHomunculus, db: &HomunculusDb) {
    let (max, min) = (&db.emax, &db.emin);
    hom.max_hp = hom.max_hp.wrapping_add(c.rnd_value(min.hp as i32, max.hp as i32) as u32);
    hom.max_sp = hom.max_sp.wrapping_add(c.rnd_value(min.sp as i32, max.sp as i32) as u32);
    hom.str += 10 * c.rnd_value(min.str as i32, max.str as i32);
    hom.agi += 10 * c.rnd_value(min.agi as i32, max.agi as i32);
    hom.vit += 10 * c.rnd_value(min.vit as i32, max.vit as i32);
    hom.int_ += 10 * c.rnd_value(min.int_ as i32, max.int_ as i32);
    hom.dex += 10 * c.rnd_value(min.dex as i32, max.dex as i32);
    hom.luk += 10 * c.rnd_value(min.luk as i32, max.luk as i32);
}

/// homunculus.cpp hom_evolution: the homunculus becomes its evolution class (bonuses, intimacy reset). 0 = failure.
pub fn hom_evolution(c: &Ctx, hd_id: u32) -> i32 {
    let Some(hd) = get(c, hd_id) else { return 0 };
    let Some(db) = hd.db() else { return 0 };
    if db.evo_class == 0 || hd.homunculus.class_ as i32 == db.evo_class {
        if let Some(u) = crate::unit::get(c, hd_id) {
            crate::clif::emotion(c, &u, ET_SCRATCH);
        }
        return 0;
    }
    if !has_master(c, &hd) {
        return 0;
    }
    let aid = hd.master;
    let evo_class = db.evo_class;
    if !hom_change_class(c, hd_id, evo_class) {
        log::error!("hom_evolution: Can't evolve homunc from {} to {}", hd.homunculus.class_, evo_class);
        return 0;
    }
    // Apply evolution bonuses
    let Some(mut hd) = get(c, hd_id) else { return 0 };
    let Some(db) = hd.db() else { return 0 };
    let mut hom = hd.homunculus.clone();
    apply_evolution_bonus(c, &mut hom, db);
    hom.intimacy = homunculus_evo_intimacy_reset();
    hd.homunculus = hom;
    put(c, &hd);
    if let Some(u) = crate::unit::get(c, hd_id) {
        crate::mob::clif::class_change(c, &u, evo_class, crate::clif::Target::Area, None);
    }
    // status_Calc flag&1 will make current HP/SP be reloaded from hom structure
    with(c, hd_id, |hd| {
        hd.homunculus.hp = hd.battle_status.hp;
        hd.homunculus.sp = hd.battle_status.sp;
    });
    status::status_calc_homunculus(c, hd_id, SCO_FIRST);
    clif_hominfo(c, aid, hd_id, 0);
    // Official servers don't recaculate the skill tree after evolution
    // but we do it for convenience
    hom_calc_skilltree(c, hd_id);
    if hom_setting() & HOMSET_NO_INSTANT_LAND_SKILL == 0 {
        crate::skillunit::skill_unit_move_id(c, hd_id, c.tick(), 1); // apply land skills immediately
    }
    1
}

/// homunculus.cpp hom_mutate: an evolved homunculus becomes a homunculus S of class `homun_id`. 0 = failure.
pub fn hom_mutate(c: &Ctx, hd_id: u32, homun_id: i32) -> i32 {
    let Some(hd) = get(c, hd_id) else { return 0 };
    let m_class = hom_class2mapid(hd.homunculus.class_ as i32);
    let m_id = hom_class2mapid(homun_id);
    if m_class == -1 || m_id == -1 || m_class & HOM_EVO == 0 || m_id & HOM_S == 0 {
        if let Some(u) = crate::unit::get(c, hd_id) {
            crate::clif::emotion(c, &u, ET_SCRATCH);
        }
        return 0;
    }
    if !has_master(c, &hd) {
        return 0;
    }
    let aid = hd.master;
    let prev_class = hd.homunculus.class_;
    if !hom_change_class(c, hd_id, homun_id) {
        log::error!("hom_mutate: Can't evolve homunc from {} to {}", hd.homunculus.class_, homun_id);
        return 0;
    }
    if let Some(u) = crate::unit::get(c, hd_id) {
        crate::mob::clif::class_change(c, &u, homun_id, crate::clif::Target::Area, None);
    }
    // status_Calc flag&1 will make current HP/SP be reloaded from hom structure
    with(c, hd_id, |hd| {
        hd.homunculus.hp = hd.battle_status.hp;
        hd.homunculus.sp = hd.battle_status.sp;
        hd.homunculus.prev_class = prev_class;
    });
    status::status_calc_homunculus(c, hd_id, SCO_FIRST);
    clif_hominfo(c, aid, hd_id, 0);
    // Official servers don't recaculate the skill tree after evolution
    // but we do it for convenience
    hom_calc_skilltree(c, hd_id);
    if hom_setting() & HOMSET_NO_INSTANT_LAND_SKILL == 0 {
        crate::skillunit::skill_unit_move_id(c, hd_id, c.tick(), 1); // apply land skills immediately
    }
    1
}

/// homunculus.cpp hom_gainexp: EXP for the homunculus (level ups, the level-up effect and a full heal).
pub fn hom_gainexp(c: &Ctx, hd_id: u32, exp: u64) {
    let Some(mut hd) = get(c, hd_id) else { return };
    if hd.homunculus.vaporize != 0 {
        return;
    }
    let m_class = hom_class2mapid(hd.homunculus.class_ as i32);
    if m_class == -1 {
        log::error!("hom_gainexp: Invalid class {}. ", hd.homunculus.class_);
        return;
    }
    if hd.exp_next == 0 || (m_class & HOM_REG != 0 && hd.homunculus.level as i32 >= hom_max_level()) || (m_class & HOM_S != 0 && hd.homunculus.level as i32 >= HOM_S_MAX_LEVEL) {
        hd.homunculus.exp = 0;
        put(c, &hd);
        return;
    }
    hd.homunculus.exp = hd.homunculus.exp.wrapping_add(exp);
    put(c, &hd);
    if has_master(c, &hd) && hd.homunculus.exp < hd.exp_next {
        clif_homunculus_updatestatus(c, hd.master, crate::status::consts::SP_BASEEXP);
        return;
    }
    // Do the levelup(s)
    loop {
        let Some(h) = get(c, hd_id) else { return };
        if h.homunculus.exp <= h.exp_next {
            break;
        }
        // Max level reached or error
        if hom_levelup(c, hd_id) == 0 {
            break;
        }
    }
    if let Some(mut h) = get(c, hd_id) {
        if h.exp_next == 0 {
            h.homunculus.exp = 0;
            put(c, &h);
        }
    }
    if let Some(u) = crate::unit::get(c, hd_id) {
        crate::clif::specialeffect(c, &u, crate::data::constant("EF_HO_UP").unwrap_or(0) as i32, crate::clif::Target::Area);
    }
    status::status_calc_homunculus(c, hd_id, SCO_NONE);
    crate::status::hp::status_percent_heal(c, hd_id, 100, 100, 0);
}

/// homunculus.cpp hom_increase_intimacy: the new intimacy (homunculus_friendly_rate applied, at most 100000).
pub fn hom_increase_intimacy(hd: &mut HomData, value: u32) -> i32 {
    let mut value = value;
    if homunculus_friendly_rate() != 100 {
        value = (value * homunculus_friendly_rate()) / 100;
    }
    if hd.homunculus.intimacy.wrapping_add(value) <= 100000 {
        hd.homunculus.intimacy = hd.homunculus.intimacy.wrapping_add(value);
    } else {
        hd.homunculus.intimacy = 100000;
    }
    hd.homunculus.intimacy as i32
}

/// homunculus.cpp hom_decrease_intimacy: the new intimacy (at least 0).
pub fn hom_decrease_intimacy(hd: &mut HomData, value: u32) -> i32 {
    if hd.homunculus.intimacy >= value {
        hd.homunculus.intimacy -= value;
    } else {
        hd.homunculus.intimacy = 0;
    }
    hd.homunculus.intimacy as i32
}

/// homunculus.cpp hom_heal: the master sees the new HP / SP (status_heal / status_damage of a homunculus).
pub fn hom_heal(c: &Ctx, hd_id: u32, hp: bool, sp: bool) {
    let Some(hd) = get(c, hd_id) else { return };
    if !has_master(c, &hd) {
        return;
    }
    if hp {
        clif_homunculus_updatestatus(c, hd.master, crate::status::consts::SP_HP);
    }
    if sp {
        clif_homunculus_updatestatus(c, hd.master, crate::status::consts::SP_SP);
    }
}

/// homunculus.cpp hom_save: HP / SP and the running cooldowns go into the struct, which is sent to the char server.
pub fn hom_save(c: &Ctx, hd_id: u32) {
    let Some(mut hd) = get(c, hd_id) else { return };
    // copy data that must be saved in homunculus struct ( hp / sp )
    //Do not check for max_hp/max_sp caps as current could be higher to max due
    //to status changes/skills (they will be capped as needed upon stat
    //calculation on login)
    hd.homunculus.hp = hd.battle_status.hp;
    hd.homunculus.sp = hd.battle_status.sp;
    // Clear skill cooldown array.
    hd.homunculus.scd = vec![Default::default(); MAX_SKILLCOOLDOWN];
    // Store current cooldown entries.
    let tick = c.tick();
    let mut count = 0usize;
    for e in hd.scd_entries() {
        // timer == nullptr || timer->func != skill_blockhomun_end || DIFF_TICK(timer->tick, tick) < 0: skipped
        let Some(t) = c.db().timer().id().find(e.timer) else { continue };
        if t.func != Func::SkillBlockhomunEnd as u16 || t.due - tick < 0 {
            continue;
        }
        if count >= MAX_SKILLCOOLDOWN {
            break; // (hd->scd never holds more: skill_blockhomun_start refuses the 21st)
        }
        hd.homunculus.scd[count] = int_homun::HomScd { skill_id: e.skill_id, tick: t.due - tick };
        count += 1;
    }
    put(c, &hd);
    int_homun::intif_homunculus_requestsave(hd.master, &hd.homunculus);
}

// ---------------------------------------------------------------------------------------------- menu / food / hunger

/// homunculus.cpp hom_menu (CZ_COMMAND_MER): 0 information (nothing), 1 feed, 2 delete.
pub fn hom_menu(c: &Ctx, aid: u32, type_: i32) {
    let Some(hd) = of_master(c, aid) else { return };
    match type_ {
        0 => {}
        1 => {
            hom_food(c, aid, hd.id);
        }
        2 => {
            hom_delete(c, hd.id);
        }
        _ => log::error!("hom_menu : unknown menu choice : {type_}"),
    }
}

/// homunculus.cpp hom_food: the master feeds the homunculus its food (intimacy by hunger, the emotion, the packets). A
/// homunculus fed down to no intimacy is deleted. 1 = not fed.
pub fn hom_food(c: &Ctx, aid: u32, hd_id: u32) -> i32 {
    if sd_of(c, aid).is_none() {
        return 1;
    }
    let Some(hd) = get(c, hd_id) else { return 1 };
    if hd.homunculus.vaporize != 0 {
        return 1;
    }
    let food_id = hd.db().map(|d| d.food_id).unwrap_or(0);
    let i = crate::inventory::pc_search_inventory(c, aid, food_id as u32);
    if i < 0 {
        clif_hom_food(c, aid, food_id, false);
        return 1;
    }
    // pc_delitem(sd, i, 1, 0, 0, LOG_TYPE_CONSUME)
    crate::inventory::pc_delitem(c, aid, i as i32, 1, 0, 0, crate::map_log::LOG_TYPE_CONSUME);
    let Some(mut hd) = get(c, hd_id) else { return 1 };
    let emotion;
    if hd.homunculus.hunger >= 91 {
        hom_decrease_intimacy(&mut hd, 50);
        emotion = ET_KEK;
    } else if hd.homunculus.hunger >= 76 {
        hom_decrease_intimacy(&mut hd, 5);
        emotion = ET_PROFUSELY_SWEAT;
    } else if hd.homunculus.hunger >= 26 {
        hom_increase_intimacy(&mut hd, 75);
        emotion = ET_DELIGHT;
    } else if hd.homunculus.hunger >= 11 {
        hom_increase_intimacy(&mut hd, 100);
        emotion = ET_DELIGHT;
    } else {
        hom_increase_intimacy(&mut hd, 50);
        emotion = ET_DELIGHT;
    }
    hd.homunculus.hunger += 10; //dunno increase value for each food
    if hd.homunculus.hunger > 100 {
        hd.homunculus.hunger = 100;
    }
    put(c, &hd);
    crate::map_log::log_feeding(c, aid, crate::map_log::LOG_FEED_HOMUNCULUS, food_id as u32);
    if let Some(u) = crate::unit::get(c, hd_id) {
        crate::clif::emotion(c, &u, emotion);
    }
    clif_send_homdata(c, hd_id, SP_HUNGRY);
    clif_send_homdata(c, hd_id, SP_INTIMATE);
    clif_hom_food(c, aid, food_id, true);
    // Too much food :/
    if get(c, hd_id).map(|h| h.homunculus.intimacy == 0).unwrap_or(false) {
        return match of_master(c, aid) {
            Some(h) => hom_delete(c, h.id),
            None => 0,
        };
    }
    0
}

/// The hunger timer's delay: homunculus_starving_delay while starving (hunger at most homunculus_starving_rate), the
/// database's HungryDelay otherwise.
fn hunger_delay(hd: &HomData) -> i32 {
    if homunculus_starving_rate() > 0 && hd.homunculus.hunger as i32 <= homunculus_starving_rate() {
        homunculus_starving_delay()
    } else {
        hd.db().map(|d| d.hungry_delay).unwrap_or(60000) // Every 20 seconds if hunger <= 10
    }
}

/// homunculus.cpp hom_hungry (Func::HomHungry; target = the master's account id): hunger goes down by one (emotions
/// at 25 / 75 / 10 and below, autofeed), a starving homunculus loses 100 intimacy (and is deleted without any left),
/// and the timer starts again.
pub fn hom_hungry(c: &Ctx, tid: u64, tick: Tick, id: u32) -> i32 {
    if sd_of(c, id).is_none() {
        return 1;
    }
    if hom_id_of(c, id) == 0 {
        return 1;
    }
    let Some(mut hd) = of_master(c, id) else { return 1 };
    if hd.hungry_timer != tid {
        log::error!("hom_hungry_timer {} != {}", hd.hungry_timer, tid);
        return 0;
    }
    hd.hungry_timer = 0;
    hd.homunculus.hunger -= 1;
    put(c, &hd);
    let hd_id = hd.id;
    if let Some(u) = crate::unit::get(c, hd_id) {
        if hd.homunculus.hunger <= 10 {
            crate::clif::emotion(c, &u, ET_FRET);
        } else if hd.homunculus.hunger == 25 {
            crate::clif::emotion(c, &u, ET_SCRATCH);
        } else if hd.homunculus.hunger == 75 {
            crate::clif::emotion(c, &u, ET_OK);
        }
    }
    if feature_homunculus_autofeed() && hd.homunculus.autofeed && hd.homunculus.hunger as i32 <= feature_homunculus_autofeed_rate() {
        hom_food(c, id, hd_id);
    }
    let Some(mut hd) = get(c, hd_id) else { return 0 };
    if hd.homunculus.hunger < 0 {
        hd.homunculus.hunger = 0;
        // Delete the homunculus if intimacy <= 100
        let left = hom_decrease_intimacy(&mut hd, 100);
        put(c, &hd);
        if left == 0 {
            return hom_delete(c, hd_id);
        }
        clif_send_homdata(c, hd_id, SP_INTIMATE);
    }
    clif_send_homdata(c, hd_id, SP_HUNGRY);
    let Some(mut hd) = get(c, hd_id) else { return 0 };
    let delay = hunger_delay(&hd);
    hd.hungry_timer = timer::add(c, tick + delay as Tick, Func::HomHungry, id, 0); //simple Fix albator
    put(c, &hd);
    0
}

/// homunculus.cpp hom_hungry_timer_delete.
pub fn hom_hungry_timer_delete(c: &Ctx, hd_id: u32) -> i32 {
    let Some(mut hd) = get(c, hd_id) else { return 0 };
    if hd.hungry_timer != 0 {
        timer::delete_checked(c, hd.hungry_timer, Func::HomHungry);
        hd.hungry_timer = 0;
        put(c, &hd);
    }
    1
}

// ---------------------------------------------------------------------------------------------- rename

/// homunculus.cpp hom_change_name (CZ_RENAME_MER): the char server checks the name (intif_rename_hom). 1 = refused
/// here.
pub fn hom_change_name(c: &Ctx, aid: u32, name: &[u8]) -> i32 {
    let Some(p) = sd_of(c, aid) else { return 1 };
    let Some(hd) = of_master(c, aid) else { return 1 };
    if !hom_is_active(Some(&hd)) {
        return 1;
    }
    if hd.homunculus.rename_flag != 0 && !hom_rename() {
        return 1;
    }
    for &b in name.iter().take(crate::mob::NAME_LENGTH) {
        if b == 0 {
            break;
        }
        if b & 0xe0 == 0 || b == 0x7f {
            return 1;
        }
    }
    // intif_rename_hom → intif_rename(sd, 2, name) (0x3006): safestrncpy(…, name, NAME_LENGTH)
    let mut n: Vec<u8> = name.iter().take_while(|&&b| b != 0).take(crate::rules::NAME_MAX).copied().collect();
    n.resize(crate::mob::NAME_LENGTH, 0);
    crate::intif::send(crate::intif::Inter::NameChange { account_id: aid, char_id: p.char_id, kind: 2, name: n });
    1 // intif_rename's return
}

/// homunculus.cpp hom_change_name_ack (intif_parse_ChangeNameOk type 2).
pub fn hom_change_name_ack(c: &Ctx, aid: u32, name: Vec<u8>, flag: i32) {
    let Some(hd) = of_master(c, aid) else { return };
    if !hom_is_active(Some(&hd)) {
        return;
    }
    let name = crate::pet::normalize_name(&name); //bugreport:3032
    if flag == 0 || name.is_empty() {
        if let Some(u) = crate::unit::get(c, aid) {
            crate::clif::displaymessage(c, &u, crate::data::msg_txt(280)); // You cannot use this name
        }
        return;
    }
    // safestrncpy(hd->homunculus.name, name, NAME_LENGTH)
    let n: Vec<u8> = name.into_iter().take(crate::rules::NAME_MAX).collect();
    let text = crate::net::wire::from_latin1(&n);
    let hd_id = hd.id;
    with(c, hd_id, |hd| {
        hd.homunculus.name = text;
        hd.homunculus.rename_flag = 1;
    });
    clif_hominfo(c, aid, hd_id, 1);
    if let Some(u) = crate::unit::get(c, hd_id) {
        crate::clif::name_area(c, &u);
    }
    clif_hominfo(c, aid, hd_id, 0);
}

// ---------------------------------------------------------------------------------------------- allocation / call

/// homunculus.cpp hom_alloc: the homunculus' hom_data and unit rows (off the map, at a cell behind its master), its
/// status (SCO_FIRST). A class without a database entry asks the char server to delete the homunculus instead.
pub fn hom_alloc(c: &Ctx, aid: u32, hom: &SHomunculus) {
    let Some(_p) = sd_of(c, aid) else { return };
    let Some(sdu) = crate::unit::get(c, aid) else { return };
    let Some(homun_db) = homunculus_db().homun_search(hom.class_ as i32) else {
        log::error!("hom_alloc: unknown class [{}] for homunculus '{}', requesting deletion.", hom.class_, hom.name);
        set_hom_id(c, aid, 0);
        int_homun::intif_homunculus_requestdelete(hom.hom_id);
        return;
    };
    if let Some(old) = of_master(c, aid) {
        // rAthena asserts that sd->hd is unset or its own; one homunculus per master here
        log::error!("hom_alloc: player {aid} already has homunculus {}; it is freed", old.id);
        unit::unit_free(c, old.id, crate::unit::CLR_OUTSIGHT);
    }
    let tick = c.tick();
    let id = crate::mob::spawn::npc_get_new_npc_id(c);
    let hom = hom.clone().normalized();
    let hd = HomData {
        id,
        master: aid,
        exp_next: homunculus_db().get_nextexp(hom.level as u16),
        homunculus: hom,
        db_class: homun_db.base_class,
        base_status: Default::default(),
        battle_status: Default::default(),
        regen: Default::default(),
        // Ticks need to be initialized before adding bl to map_addiddb
        regen_tick_hp: tick,
        regen_tick_sp: tick,
        masterteleport_timer: 0,
        hungry_timer: 0,
        scd: Vec::new(),
        scd_order: Vec::new(),
        scd_buckets: 0,
        scd_next_resize: 0,
    };
    // hd->type = BL_HOM; unit_dataset(hd); hd->ud.dir = sd->ud.dir; Find a random valid pos around the player
    let u = crate::unit::Unit {
        id,
        kind: crate::map::BL_HOM,
        map: sdu.map,
        x: sdu.x,
        y: sdu.y,
        on_map: false,
        name: hd.homunculus.name.clone(),
        vd: Default::default(),
        // unit_dataset: every timer INVALID_TIMER, attackabletime / canact_tick / canmove_tick / endure_tick = now,
        // sx / sy 8
        ud: crate::unit::UnitData { dir: sdu.ud.dir, attackabletime: tick, canact_tick: tick, canmove_tick: tick, endure_tick: tick, dmg_tick: 0, sx: 8, sy: 8, ..Default::default() },
        sc: Default::default(),
        speed: 0,
        mode: 0,
        level: hd.homunculus.level as i32,
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
    // map_addiddb(hd): the unit exists (off the map) from now on
    c.db().unit().insert(u);
    put(c, &hd);
    status::status_set_viewdata(c, id, hd.homunculus.class_ as i32);
    crate::pet::ai::unit_calc_pos(c, id, sdu.x as i32, sdu.y as i32, sdu.ud.dir);
    if let Some(mut u2) = crate::unit::get(c, id) {
        u2.x = u2.ud.to_x;
        u2.y = u2.ud.to_y;
        crate::unit::save(c, &u2);
    }
    status::status_calc_homunculus(c, id, SCO_FIRST);
    with(c, id, |hd| {
        hd.hungry_timer = 0;
        hd.masterteleport_timer = 0;
    });
}

/// homunculus.cpp hom_init_timers: the hunger timer (when not running), regen unblocked, no master teleport timer.
pub fn hom_init_timers(c: &Ctx, hd_id: u32) {
    let Some(mut hd) = get(c, hd_id) else { return };
    if hd.hungry_timer == 0 {
        let delay = hunger_delay(&hd);
        hd.hungry_timer = timer::add(c, c.tick() + delay as Tick, Func::HomHungry, hd.master, 0);
    }
    hd.regen.block = 0; //Restore HP/SP block.
    hd.masterteleport_timer = 0;
    put(c, &hd);
}

/// The spawn sequence hom_call / hom_recv_data / clif_parse_LoadEndAck share: clif_spawn, ZC_CHANGESTATE_MER (ack),
/// the information twice, HP and SP, and the skill list ("For some reason, official servers send the homunculus info
/// twice, then update the HP/SP again.").
fn spawn_packets(c: &Ctx, aid: u32, hd_id: u32) {
    if let Some(u) = crate::unit::get(c, hd_id) {
        crate::clif::spawn(c, &u, false);
    }
    clif_send_homdata(c, hd_id, SP_ACK);
    clif_hominfo(c, aid, hd_id, 1);
    clif_hominfo(c, aid, hd_id, 0);
    clif_homunculus_updatestatus(c, aid, crate::status::consts::SP_HP);
    clif_homunculus_updatestatus(c, aid, crate::status::consts::SP_SP);
    clif_homskillinfoblock(c, hd_id);
}

/// map_addblock for the homunculus (false = failed).
fn addblock(c: &Ctx, hd_id: u32) -> bool {
    let Some(mut u) = crate::unit::get(c, hd_id) else { return false };
    crate::unit::addblock(c, &mut u)
}

/// sc_start(sd, sd, SC_HOMUN_TIME, 100, 1, skill_get_time(AM_CALLHOMUN, 1)) (RENEWAL).
fn start_homun_time(c: &Ctx, aid: u32) {
    if let Some(su) = crate::unit::get(c, aid) {
        let d = crate::db::skills::skill_get_time(crate::db::skills::ids::AM_CALLHOMUN, 1) as i64;
        crate::status::sc::sc_start(c, Some(&su), &su, crate::status::consts::SC_HOMUN_TIME, 100, 1, d);
    }
}

/// "Apply any active skill cooldowns" (homunculus.cpp:1157-1159, 1322-1324): skill_blockhomun_start for every entry of
/// hd->scd. Deliberate difference: rAthena passes `entry.second`, the running timer's id, as the duration (a timer
/// slot index of its timer_data array, a small number that depends on every timer the map server ever allocated);
/// the module's timer ids are a global auto-increment and cannot reproduce that number, so the cooldown's remaining
/// time is passed instead (what the code means to do). Only reached when a homunculus with running cooldowns is
/// called back or resurrected.
fn reapply_scd(c: &Ctx, hd_id: u32) {
    let Some(hd) = get(c, hd_id) else { return };
    for e in hd.scd_entries() {
        let left = crate::timer::due(c, e.timer).map(|due| due - c.tick()).unwrap_or(0);
        cooldown::skill_blockhomun_start(c, hd_id, e.skill_id, left);
    }
}

/// homunculus.cpp hom_call (AM_CALLHOMUN): creates a homunculus (random class) when the player has none, loads it
/// when not loaded, or calls a vaporized one back (spawned next to its master, or warped to it). False = failure.
pub fn hom_call(c: &Ctx, aid: u32) -> bool {
    if sd_of(c, aid).is_none() {
        return false;
    }
    if hom_id_of(c, aid) == 0 {
        //Create a new homun.
        let class_ = HM_CLASS_BASE + c.rnd_value(0, 7);
        return hom_create_request(c, aid, class_);
    }
    // If homunc not yet loaded, load it
    let Some(hd) = of_master(c, aid) else {
        return int_homun::intif_homunculus_requestload(aid, hom_id_of(c, aid)) > 0;
    };
    if hd.homunculus.vaporize == 0 {
        return false; //Can't use this if homun wasn't vaporized.
    }
    if hd.homunculus.vaporize as i32 == HOM_ST_MORPH {
        return false; // Can't call homunculus (morph state).
    }
    let hd_id = hd.id;
    hom_init_timers(c, hd_id);
    with(c, hd_id, |hd| hd.homunculus.vaporize = HOM_ST_ACTIVE as i16);
    let on_map = crate::unit::get(c, hd_id).map(|u| u.on_map).unwrap_or(false);
    let Some(sdu) = crate::unit::get(c, aid) else { return false };
    if !on_map {
        //Spawn him
        if let Some(mut u) = crate::unit::get(c, hd_id) {
            u.x = sdu.x;
            u.y = sdu.y;
            u.map = sdu.map;
            crate::unit::save(c, &u);
        }
        if !addblock(c, hd_id) {
            return false;
        }
        spawn_packets(c, aid, hd_id);
        crate::status::calc_bl(c, hd_id, 1u64 << crate::status::consts::SCB_SPEED, SCO_NONE);
        hom_save(c, hd_id);
    } else {
        //Warp him to master.
        unit::unit_warp(c, hd_id, sdu.map as i32, sdu.x, sdu.y, crate::unit::CLR_OUTSIGHT);
    }
    // Apply any active skill cooldowns.
    reapply_scd(c, hd_id);
    // RENEWAL
    start_homun_time(c, aid);
    true
}

/// homunculus.cpp hom_recv_data (intif_parse_CreateHomunculus / intif_parse_RecvHomunculusData): the char server's
/// homunculus (flag 0: failed to load / create). A new homunculus is healed fully; an active one with HP is spawned
/// next to its master when the master is on a map. 0 = failure.
pub fn hom_recv_data(c: &Ctx, account_id: u32, sh: SHomunculus, flag: i32) -> i32 {
    let Some(p) = sd_of(c, account_id) else { return 0 };
    let mut sh = sh.normalized();
    if p.char_id != sh.char_id {
        if hom_id_of(c, account_id) == sh.hom_id {
            sh.char_id = p.char_id; //Correct char id.
        } else {
            return 0;
        }
    }
    if flag == 0 {
        // Failed to load
        set_hom_id(c, account_id, 0);
        return 0;
    }
    let mut created = false;
    if hom_id_of(c, account_id) == 0 {
        //Hom just created.
        set_hom_id(c, account_id, sh.hom_id);
        created = true;
    }
    if let Some(mut hd) = of_master(c, account_id) {
        //uh? Overwrite the data.
        hd.homunculus = sh;
        put(c, &hd);
    } else {
        hom_alloc(c, account_id, &sh);
    }
    let Some(hd) = of_master(c, account_id) else { return 0 };
    let hd_id = hd.id;
    if created {
        crate::status::hp::status_percent_heal(c, hd_id, 100, 100, 0);
    }
    let Some(hd) = get(c, hd_id) else { return 0 };
    let hd_on_map = crate::unit::get(c, hd_id).map(|u| u.on_map).unwrap_or(false);
    let sd_on_map = crate::unit::get(c, account_id).map(|u| u.on_map).unwrap_or(false);
    if hd.homunculus.hp != 0 && hd.homunculus.vaporize == 0 && !hd_on_map && sd_on_map {
        if !addblock(c, hd_id) {
            return 0;
        }
        spawn_packets(c, account_id, hd_id);
        hom_init_timers(c, hd_id);
        // RENEWAL
        start_homun_time(c, account_id);
    }
    // Apply any active skill cooldowns.
    if let Some(hd) = get(c, hd_id) {
        for s in hd.homunculus.scd.iter().take(MAX_SKILLCOOLDOWN) {
            cooldown::skill_blockhomun_start(c, hd_id, s.skill_id, s.tick);
        }
    }
    1
}

/// homunculus.cpp hom_create_request: asks the char server to create a homunculus of `class_` (level 1, hunger 32,
/// intimacy 2100, the database's base stats). False when the class has no database entry.
pub fn hom_create_request(c: &Ctx, aid: u32, class_: i32) -> bool {
    let Some(p) = sd_of(c, aid) else { return false };
    let Some(homun_db) = homunculus_db().homun_search(class_) else { return false };
    let base = homun_db.base;
    //Initial data
    let homun = SHomunculus {
        // safestrncpy(homun.name, homun_db->name, NAME_LENGTH-1): at most 22 characters
        name: homun_db.name.chars().take(crate::rules::NAME_MAX - 1).collect(),
        class_: class_ as i16,
        level: 1,
        hunger: 32,    //32%
        intimacy: 2100, //21/1000
        char_id: p.char_id,
        hp: 10,
        max_hp: base.hp,
        max_sp: base.sp,
        str: base.str as i32 * 10,
        agi: base.agi as i32 * 10,
        vit: base.vit as i32 * 10,
        int_: base.int_ as i32 * 10,
        dex: base.dex as i32 * 10,
        luk: base.luk as i32 * 10,
        ..Default::default()
    };
    // Request homunculus creation
    int_homun::intif_homunculus_create(aid, &homun);
    true
}

/// homunculus.cpp hom_ressurect (AM_RESURRECTHOMUN): a dead homunculus comes back at (x, y) with `per` % HP. Loads a
/// homunculus that is not loaded (the answer's hom_recv_data does the rest). 0 = failure.
pub fn hom_ressurect(c: &Ctx, aid: u32, per: u8, x: i16, y: i16) -> i32 {
    if sd_of(c, aid).is_none() {
        return 0;
    }
    if hom_id_of(c, aid) == 0 {
        return 0; // no homunculus
    }
    let Some(hd) = of_master(c, aid) else {
        //Load homun data;
        return int_homun::intif_homunculus_requestload(aid, hom_id_of(c, aid));
    };
    if hd.homunculus.vaporize as i32 == HOM_ST_REST {
        return 0; // vaporized homunculi need to be 'called'
    }
    if !hom_isdead(&hd) {
        return 0; // already alive
    }
    let hd_id = hd.id;
    hom_init_timers(c, hd_id);
    let on_map = crate::unit::get(c, hd_id).map(|u| u.on_map).unwrap_or(false);
    if !on_map {
        //Add it back to the map.
        let m = crate::unit::get(c, aid).map(|u| u.map).unwrap_or(0);
        if let Some(mut u) = crate::unit::get(c, hd_id) {
            u.map = m;
            u.x = x;
            u.y = y;
            crate::unit::save(c, &u);
        }
        if !addblock(c, hd_id) {
            return 0;
        }
        if let Some(u) = crate::unit::get(c, hd_id) {
            crate::clif::spawn(c, &u, false);
        }
    }
    if let Some(mut u) = crate::unit::get(c, hd_id) {
        u.ud.blockedmove = false;
        crate::unit::save(c, &u);
    }
    // Apply any active skill cooldowns.
    reapply_scd(c, hd_id);
    // RENEWAL
    start_homun_time(c, aid);
    crate::status::hp::status_revive(c, hd_id, per, 0, 0)
}

/// homunculus.cpp hom_revive (status_revive of a homunculus): the master sees it alive (the spawn packets without
/// the spawn); Eleanor starts in fighting mode.
pub fn hom_revive(c: &Ctx, hd_id: u32, _hp: u32, _sp: u32) {
    let Some(mut hd) = get(c, hd_id) else { return };
    hd.homunculus.hp = hd.battle_status.hp;
    put(c, &hd);
    if !has_master(c, &hd) {
        return;
    }
    let aid = hd.master;
    clif_send_homdata(c, hd_id, SP_ACK);
    clif_hominfo(c, aid, hd_id, 1);
    clif_hominfo(c, aid, hd_id, 0);
    clif_homunculus_updatestatus(c, aid, crate::status::consts::SP_HP);
    clif_homunculus_updatestatus(c, aid, crate::status::consts::SP_SP);
    clif_homskillinfoblock(c, hd_id);
    if hd.homunculus.class_ as i32 == MER_ELEANOR {
        if let Some(u) = crate::unit::get(c, hd_id) {
            crate::status::sc::sc_start(c, Some(&u), &u, crate::status::consts::SC_STYLE_CHANGE, 100, MH_MD_FIGHTING, crate::status::sc::INFINITE_TICK);
        }
    }
}

// ---------------------------------------------------------------------------------------------- stats

/// homunculus.cpp hom_reset_stats: level 1 with the database's base stats, no EXP, no skills (hunger and intimacy
/// stay).
pub fn hom_reset_stats(hd: &mut HomData) {
    let Some(db) = hd.db() else { return };
    let base = db.base;
    let hom = &mut hd.homunculus;
    hom.level = 1;
    hom.hp = 10;
    hom.max_hp = base.hp;
    hom.max_sp = base.sp;
    hom.str = base.str as i32 * 10;
    hom.agi = base.agi as i32 * 10;
    hom.vit = base.vit as i32 * 10;
    hom.int_ = base.int_ as i32 * 10;
    hom.dex = base.dex as i32 * 10;
    hom.luk = base.luk as i32 * 10;
    hom.exp = 0;
    hd.exp_next = homunculus_db().get_nextexp(hd.homunculus.level as u16);
    hd.homunculus.hskill = vec![HSkill::default(); MAX_HOMUNSKILL];
    hd.homunculus.skillpts = 0;
}

/// homunculus.cpp hom_shuffle (@homshuffle, homshuffle): the stats are rolled again (reset, levelled back up, the
/// evolution bonuses again); EXP, skills and skill points are kept. 0 = not active.
pub fn hom_shuffle(c: &Ctx, hd_id: u32) -> i32 {
    let Some(mut hd) = get(c, hd_id) else { return 0 };
    if !hom_is_active(Some(&hd)) {
        return 0;
    }
    let lv = hd.homunculus.level as i32;
    let exp = hd.homunculus.exp;
    let b_skill = hd.homunculus.hskill.clone();
    let skillpts = hd.homunculus.skillpts;
    //Reset values to level 1.
    hom_reset_stats(&mut hd);
    put(c, &hd);
    //Level it back up
    let mut i = 1;
    while i < lv {
        let Some(mut h) = get(c, hd_id) else { return 0 };
        if h.exp_next == 0 {
            break;
        }
        h.homunculus.exp = h.homunculus.exp.wrapping_add(h.exp_next);
        put(c, &h);
        // Should never happen, but who knows
        if hom_levelup(c, hd_id) == 0 {
            break;
        }
        i += 1;
    }
    let Some(mut hd) = get(c, hd_id) else { return 0 };
    if let Some(db) = hd.db() {
        if hd.homunculus.class_ as i32 == db.evo_class {
            //Evolved bonuses
            let mut hom = hd.homunculus.clone();
            apply_evolution_bonus(c, &mut hom, db);
            hd.homunculus = hom;
        }
    }
    hd.homunculus.exp = exp;
    hd.homunculus.hskill = b_skill;
    hd.homunculus.skillpts = skillpts;
    put(c, &hd);
    clif_homskillinfoblock(c, hd_id);
    status::status_calc_homunculus(c, hd_id, SCO_NONE);
    crate::status::hp::status_percent_heal(c, hd_id, 100, 100, 0);
    if let Some(u) = crate::unit::get(c, hd_id) {
        crate::clif::specialeffect(c, &u, crate::data::constant("EF_HO_UP").unwrap_or(0) as i32, crate::clif::Target::Area);
    }
    1
}

/// homunculus.cpp hom_reload: the databases are re-read (the module's data is embedded: nothing changes at run time).
pub fn hom_reload(_c: &Ctx) {
    // homunculus_db.load(); homun_exp_db.reload(): the embedded tables are the loaded ones
}

// ---------------------------------------------------------------------------------------------- the master's hooks

/// pc.cpp pc_reg_received (pc.cpp:2462-2463): `if (sd->status.hom_id > 0) intif_homunculus_requestload(…)`. The
/// module's login handles the char server's answers in place in their order (pc.rs authok): the homunculus' answer
/// (mapif_parse_homunculus_load → intif_parse_RecvHomunculusData → hom_recv_data) comes right after the pet's, before
/// the skill cooldowns' and the bonus scripts', as on the oracle's one char-server socket.
pub fn pc_reg_received(c: &Ctx, aid: u32) {
    let hom_id = hom_id_of(c, aid);
    if hom_id > 0 {
        match int_homun::mapif_homunculus_load(c, hom_id) {
            Some(hd) => hom_recv_data(c, aid, hd, 1),
            None => hom_recv_data(c, aid, SHomunculus::default(), 0),
        };
    }
}

/// clif.cpp clif_parse_LoadEndAck (clif.cpp:10867-10883): an active homunculus enters the map with its master.
pub fn load_end_ack(c: &Ctx, aid: u32) {
    let Some(hd) = of_master(c, aid) else { return };
    if !hom_is_active(Some(&hd)) {
        return;
    }
    let hd_id = hd.id;
    if !addblock(c, hd_id) {
        return;
    }
    spawn_packets(c, aid, hd_id);
    crate::status::calc_bl(c, hd_id, 1u64 << crate::status::consts::SCB_SPEED, SCO_NONE);
    if hom_setting() & HOMSET_NO_INSTANT_LAND_SKILL == 0 {
        crate::skillunit::skill_unit_move_id(c, hd_id, c.tick(), 1); // apply land skills immediately
    }
}

/// clif.cpp clif_parse_LoadEndAck for a new connection (clif.cpp:10942-10943): `if (hom_is_active(sd->hd))
/// hom_init_timers(sd->hd)`.
pub fn load_end_ack_connect_new(c: &Ctx, aid: u32) {
    if let Some(hd) = of_master(c, aid) {
        if hom_is_active(Some(&hd)) {
            hom_init_timers(c, hd.id);
        }
    }
}

/// clif.cpp clif_parse_LoadEndAck (clif.cpp:11023-11036): the homunculus autofeed switch of the configuration
/// window (homunculus_autofeed_always: sent when the player has a homunculus, else only when it is on).
pub fn load_end_ack_autofeed(c: &Ctx, u: &crate::unit::Unit) {
    let hd = of_master(c, u.id);
    if homunculus_autofeed_always() {
        // Always send ON or OFF
        match hd {
            Some(hd) if feature_homunculus_autofeed() => crate::pc::configuration(c, u, crate::pc::CONFIG_HOMUNCULUS_AUTOFEED, hd.homunculus.autofeed),
            _ => crate::pc::configuration(c, u, crate::pc::CONFIG_HOMUNCULUS_AUTOFEED, false),
        }
    } else if let Some(hd) = hd {
        // Only send when enabled
        if feature_homunculus_autofeed() && hd.homunculus.autofeed {
            crate::pc::configuration(c, u, crate::pc::CONFIG_HOMUNCULUS_AUTOFEED, true);
        }
    }
}

/// pc.cpp pc_setpos (pc.cpp:7142-7157): an active homunculus moves with its master (warp statuses end, it re-enters
/// the map at the master's LoadEndAck).
pub fn pc_setpos(c: &Ctx, aid: u32, changemap: bool, m: u16, x: i16, y: i16) {
    let Some(hd) = of_master(c, aid) else { return };
    if !hom_is_active(Some(&hd)) {
        return;
    }
    let hd_id = hd.id;
    let flag = if changemap { crate::status::consts::SCF_REMOVEFROMHOMONMAPWARP } else { crate::status::consts::SCF_REMOVEFROMHOMONWARP };
    // status_db.removeByStatusFlag(sd->hd, { flag })
    let has = |t| crate::status::sc::has_sce(c, hd_id, t);
    for t in crate::db::misc::status::status_db().remove_by_status_flag(&has, &[flag]) {
        crate::status::sc::status_change_end(c, hd_id, t, 0);
    }
    if hom_delay_reset_warp() {
        cooldown::skill_blockhomun_clear(c, hd_id);
    }
    let dir = crate::unit::get(c, aid).map(|u| u.ud.dir).unwrap_or(0);
    if let Some(mut hu) = crate::unit::get(c, hd_id) {
        hu.map = m;
        hu.x = x;
        hu.ud.to_x = x;
        hu.y = y;
        hu.ud.to_y = y;
        hu.ud.dir = dir;
        crate::unit::save(c, &hu);
    }
}

/// chrif.cpp chrif_save (chrif.cpp:332-333): `if (hom_is_active(sd->hd)) hom_save(sd->hd)`.
pub fn chrif_save(c: &Ctx, aid: u32) {
    if let Some(hd) = of_master(c, aid) {
        if hom_is_active(Some(&hd)) {
            hom_save(c, hd.id);
        }
    }
}

/// unit.cpp unit_remove_map_pc (unit.cpp:3875-3876): `if (hom_is_active(sd->hd)) unit_remove_map(sd->hd, clrtype)`
/// (CLR_RESPAWN / CLR_TELEPORT already turned into CLR_OUTSIGHT by the caller).
pub fn unit_remove_map_pc(c: &Ctx, aid: u32, clrtype: u8) {
    if let Some(hd) = of_master(c, aid) {
        if hom_is_active(Some(&hd)) {
            unit::unit_remove_map(c, hd.id, clrtype);
        }
    }
}

/// unit.cpp unit_free_pc (unit.cpp:3895-3896): `if (sd->hd) unit_free(sd->hd, CLR_OUTSIGHT)`.
pub fn unit_free_pc(c: &Ctx, aid: u32) {
    if let Some(hd) = of_master(c, aid) {
        unit::unit_free(c, hd.id, crate::unit::CLR_OUTSIGHT);
    }
}

/// pc.cpp pc_dead (pc.cpp:9849-9850): an active homunculus with homunculus_auto_vapor % HP or more is vaporized
/// (HOM_ST_ACTIVE: no HP check).
pub fn pc_dead(c: &Ctx, aid: u32) {
    let Some(hd) = of_master(c, aid) else { return };
    if hom_is_active(Some(&hd)) && homunculus_auto_vapor() != 0 && crate::pet::get_percentage(hd.battle_status.hp, hd.battle_status.max_hp) as i32 >= homunculus_auto_vapor() {
        hom_vaporize(c, aid, HOM_ST_ACTIVE);
    }
}

/// pc.cpp pc_jobchange (pc.cpp:9468-9469) and pc_resetskill (pc.cpp:11043-11044): an active homunculus is vaporized
/// (HOM_ST_ACTIVE) when `vaporize` holds (the job change: the new job has AM_CALLHOMUN; the skill reset: AM_CALLHOMUN
/// is gone).
pub fn vaporize_if_active(c: &Ctx, aid: u32) {
    if is_active_of(c, aid) {
        hom_vaporize(c, aid, HOM_ST_ACTIVE);
    }
}

/// pc.cpp pc_skillup (pc.cpp:9146-9148): `else if (sd->hd && SKILL_CHK_HOMUN(skill_id)) hom_skillup(sd->hd,
/// skill_id)`. True when the request was the homunculus'.
pub fn pc_skillup(c: &Ctx, aid: u32, skill_id: u16) -> bool {
    if !crate::db::skills::skill_chk_homun(skill_id) {
        return false;
    }
    let Some(hd) = of_master(c, aid) else { return false };
    hom_skillup(c, hd.id, skill_id);
    true
}

/// mob.cpp mob_dead's EXP share (mob.cpp:3199-3201, RENEWAL): `if (base_exp && tmpsd[i] && tmpsd[i]->hd)
/// hom_gainexp(tmpsd[i]->hd, base_exp * battle_config.homunculus_exp_gain / 100)` (a vaporized homunculus gets
/// nothing: hom_gainexp returns). The pre-renewal MDLF_HOMUN rule is not compiled.
pub fn mob_dead_gainexp(c: &Ctx, aid: u32, base_exp: u64) {
    if base_exp == 0 {
        return;
    }
    let Some(hd) = of_master(c, aid) else { return };
    hom_gainexp(c, hd.id, base_exp.wrapping_mul(homunculus_exp_gain() as u64) / 100); // Homunculus only receive 10% of EXP
}

/// mob.cpp mob_log_damage (mob.cpp:2692-2698): a homunculus' damage is logged as MDLF_HOMUN under its master's
/// char id (0 without a master).
pub fn mob_log_damage_char_id(c: &Ctx, hd_id: u32) -> u32 {
    get(c, hd_id).and_then(|hd| sd_of(c, hd.master)).map(|p| p.char_id).unwrap_or(0)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn classes_and_grades() {
        assert_eq!(hom_class2type(6001), HT_REG);
        assert_eq!(hom_class2type(6009), HT_EVO);
        assert_eq!(hom_class2type(6016), HT_EVO);
        assert_eq!(hom_class2type(6052), HT_S);
        // rAthena quirk: an unknown class' mapid is -1, whose bits hold HOM_REG | HOM_EVO: HT_EVO, never HT_INVALID
        assert_eq!(hom_class2type(6020), HT_EVO);
        assert_eq!(hom_skill_get_index(8001), 0);
        assert_eq!(hom_skill_get_index(8059), 58);
        assert_eq!(hom_skill_get_index(8060), -1);
        assert_eq!(hom_intimacy_intimacy2grade(99), HOMGRADE_HATE_WITH_PASSION);
        assert_eq!(hom_intimacy_intimacy2grade(2100), HOMGRADE_AWKWARD);
        assert_eq!(hom_intimacy_intimacy2grade(91100), HOMGRADE_LOYAL);
        assert_eq!(hom_intimacy_grade2intimacy(HOMGRADE_CORDIAL), 75100);
        let mut h = SHomunculus { level: 57, ..Default::default() };
        apply_homun_level_statweight(&mut h);
        assert_eq!((h.str_value, h.luk_value), (5, 5));
    }
}
