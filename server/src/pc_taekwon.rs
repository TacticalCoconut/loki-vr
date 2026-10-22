//! The Taekwon family's parts of map_session_data and their packets (rAthena e985006), one owner for each field:
//! - pc.hpp `sd->feel_map[MAX_PC_FEELHATE]` { m, index } and `sd->hate_mob[MAX_PC_FEELHATE]` (Star Gladiator: the
//!   Feeling maps and Hatred monsters), loaded from the sg_info[] registers by pc_reg_received and changed by
//!   clif_parse_FeelSaveOk (SG_FEEL), pc_set_hate_mob (SG_HATE), pc_resetfeel / pc_resethate. Every reader of the
//!   feel / hate targets (skill_check_condition_castbegin / castend, the SG_KNOWLEDGE / SC_KNOWLEDGE feel-map test,
//!   battle_attack_sc_bonus, mob_dead) goes through [`feel_map`], [`feel_index`] and [`hate_mob`].
//! - pc.hpp `sd->mission_mobid` / `mission_count` (TK_MISSION), loaded by pc_reg_received for the Taekwon line,
//!   counted by mob_dead ([`mob_dead_mission`]), reset by pc_resetstate ([`pc_resetstate_mission`]).
//! - clif.cpp clif_starskill (0x20e) and its users clif_feel_info, clif_hate_info, clif_mission_info,
//!   clif_feel_hate_reset; clif_feel_req (0x253) and clif_parse_FeelSaveOk (0x254); clif_parse_NoviceDoriDori (0x1e7).
//! - common/mapindex.cpp's index table (db/map_index.txt: data/json/mapindex.json.z, tools/datagen_mods/mapindex.py):
//!   mapindex_name2id, mapindex_id2name, map_id2index and map_mapindex2mapid, which the feel registers need (they hold
//!   a mapindex, not a map id).
//!
//! Deliberately different: rAthena keeps these fields in the session and reloads them from the registers at each
//! login; here they are a `pc_taekwon` row per online character, written at pc_reg_received and removed by unit_free,
//! with the same values.

use spacetimedb::{table, Table};

use crate::clif::{self, Target};
use crate::db::jobs::ids::*;
use crate::db::skills::*;
use crate::instance_map::{instance_map, instance_mapindex};
use crate::unit::Unit;
use crate::world::Ctx;

/// mmo.hpp NAME_LENGTH.
const NAME_LENGTH: usize = 24;
/// pc.hpp MAX_PC_FEELHATE.
pub const MAX_PC_FEELHATE: usize = 3;
/// pc.hpp TKMISSIONID_VAR / TKMISSIONCOUNT_VAR.
pub const TKMISSIONID_VAR: &str = "TK_MISSION_ID";
pub const TKMISSIONCOUNT_VAR: &str = "TK_MISSION_COUNT";
/// pc.cpp sg_info[i].feel_var / hate_var.
pub const SG_FEEL_VARS: [&str; MAX_PC_FEELHATE] = ["PC_FEEL_SUN", "PC_FEEL_MOON", "PC_FEEL_STAR"];
pub const SG_HATE_VARS: [&str; MAX_PC_FEELHATE] = ["PC_HATE_MOB_SUN", "PC_HATE_MOB_MOON", "PC_HATE_MOB_STAR"];
/// pc.cpp sg_info[i].bless_id.
pub const SG_BLESS: [u16; MAX_PC_FEELHATE] = [SG_SUN_BLESS, SG_MOON_BLESS, SG_STAR_BLESS];

/// map_session_data's Taekwon family fields.
#[table(accessor = pc_taekwon)]
#[derive(Clone, Debug, PartialEq)]
pub struct PcTaekwon {
    #[primary_key]
    pub account_id: u32,
    /// sd->feel_map[i].m (-1: none) and .index (the mapindex, 0: none).
    pub feel_m: Vec<i32>,
    pub feel_index: Vec<u16>,
    /// sd->hate_mob[i] (-1: none).
    pub hate_mob: Vec<i16>,
    /// sd->mission_mobid, sd->mission_count.
    pub mission_mobid: i16,
    pub mission_count: u8,
}

/// map_session_data as pc_setnewpc leaves it (a zeroed session: feel_map[].m 0, hate_mob[] -1) before
/// pc_reg_received fills it.
fn fresh(aid: u32) -> PcTaekwon {
    PcTaekwon { account_id: aid, feel_m: vec![0; MAX_PC_FEELHATE], feel_index: vec![0; MAX_PC_FEELHATE], hate_mob: vec![-1; MAX_PC_FEELHATE], mission_mobid: 0, mission_count: 0 }
}

pub fn get(c: &Ctx, aid: u32) -> PcTaekwon {
    c.db().pc_taekwon().account_id().find(aid).unwrap_or_else(|| fresh(aid))
}

pub fn put(c: &Ctx, s: &PcTaekwon) {
    match c.db().pc_taekwon().account_id().find(s.account_id) {
        Some(old) if old == *s => {}
        Some(_) => {
            c.db().pc_taekwon().account_id().update(s.clone());
        }
        None => {
            c.db().pc_taekwon().insert(s.clone());
        }
    }
}

fn with<R>(c: &Ctx, aid: u32, f: impl FnOnce(&mut PcTaekwon) -> R) -> R {
    let mut s = get(c, aid);
    let r = f(&mut s);
    put(c, &s);
    r
}

/// unit_free: the fields go with the session.
pub fn free(c: &Ctx, aid: u32) {
    c.db().pc_taekwon().account_id().delete(aid);
}

/// sd->feel_map[i].m: the map id of the i-th Feeling map, -1 when none (or not loaded on this server).
pub fn feel_map(c: &Ctx, aid: u32, i: usize) -> i32 {
    get(c, aid).feel_m.get(i).copied().unwrap_or(-1)
}

/// sd->feel_map[i].index: the mapindex of the i-th Feeling map, 0 when none.
pub fn feel_index(c: &Ctx, aid: u32, i: usize) -> u16 {
    get(c, aid).feel_index.get(i).copied().unwrap_or(0)
}

/// sd->hate_mob[i]: the class of the i-th Hatred target, -1 when none.
pub fn hate_mob(c: &Ctx, aid: u32, i: usize) -> i32 {
    get(c, aid).hate_mob.get(i).map(|&h| h as i32).unwrap_or(-1)
}

/// sd->hate_mob[] as a whole.
pub fn hate_mobs(c: &Ctx, aid: u32) -> [i32; MAX_PC_FEELHATE] {
    let s = get(c, aid);
    std::array::from_fn(|i| s.hate_mob.get(i).map(|&h| h as i32).unwrap_or(-1))
}

/// sd->mission_mobid.
pub fn mission_mobid(c: &Ctx, aid: u32) -> i16 {
    get(c, aid).mission_mobid
}

/// sd->mission_count.
pub fn mission_count(c: &Ctx, aid: u32) -> u8 {
    get(c, aid).mission_count
}

/// pc.cpp pc_setglobalreg (a permanent character register).
fn setglobalreg(c: &Ctx, aid: u32, var: &str, v: i64) {
    crate::skilltree::set_char_reg(c, aid, var, v);
}

fn readglobalreg(c: &Ctx, aid: u32, var: &str) -> i64 {
    crate::skilltree::read_char_reg(c, aid, var)
}

// ---------------------------------------------------------------------------------------------- mapindex

#[derive(serde::Deserialize)]
pub struct MapIndex {
    /// indexes[]: index -> name ("" = none), up to max_index.
    pub names: Vec<String>,
}

crate::dataset!(mapindex_db, "mapindex", MapIndex);

/// mapindex.cpp max_index at start: the indexes above it are the instance maps' run-time ones (mapindex_addmap(-1)
/// takes the first free index from 1: db/map_index.txt has no holes, so instance_map's slot k is max_index + k).
fn max_index() -> u16 {
    mapindex_db().names.len() as u16
}

/// mapindex.cpp mapindex_name2idx (mapindex_name2id): the index of a map name (mapindex_getmapname first), 0 when
/// the name is not in mapindex_db. Instance map names (strdb_iput at mapindex_addmap, never removed) resolve to
/// their slot's index (instance_map.rs's instance_mapindex rows).
pub fn mapindex_name2id(c: &Ctx, name: &str) -> u16 {
    let n = crate::db::misc::mapindex_getmapname(name);
    // mapindex_db (strdb_iput: a later index of the same name wins)
    static BY_NAME: std::sync::OnceLock<std::collections::HashMap<String, u16>> = std::sync::OnceLock::new();
    let by_name = BY_NAME.get_or_init(|| mapindex_db().names.iter().enumerate().filter(|(_, x)| !x.is_empty()).map(|(i, x)| (x.clone(), i as u16)).collect());
    if let Some(&i) = by_name.get(&n) {
        return i;
    }
    c.db().instance_mapindex().name().find(&n).map(|r| max_index() + r.slot).unwrap_or(0)
}

/// mapindex.cpp mapindex_idx2name (mapindex_id2name): "" for an index without a map (a destroyed instance map's
/// index: mapindex_removemap clears its name).
pub fn mapindex_id2name(c: &Ctx, id: u16) -> String {
    if id < max_index() {
        return mapindex_db().names[id as usize].clone();
    }
    c.db().instance_map().iter().find(|r| max_index() + r.slot == id).map(|r| r.name).unwrap_or_default()
}

/// map.hpp map_id2index(m): `map[m].index` (map_readallmaps: mapindex_name2id(map name); instance maps: the run-time
/// index mapindex_addmap(-1) gave them).
pub fn map_id2index(c: &Ctx, m: u16) -> u16 {
    if let Some(r) = c.db().instance_map().m().find(m) {
        return max_index() + r.slot;
    }
    let Some(md) = crate::data::maps().get(m) else { return 0 };
    mapindex_name2id(c, &md.name)
}

/// map.cpp map_mapindex2mapid: the map (with cells) holding the index on this server, -1 when none.
pub fn map_mapindex2mapid(c: &Ctx, mapindex: u16) -> i32 {
    if mapindex == 0 {
        return -1;
    }
    if mapindex < max_index() {
        let name = mapindex_id2name(c, mapindex);
        return crate::data::maps().id_of(&name).map(|m| m as i32).unwrap_or(-1);
    }
    c.db().instance_map().iter().find(|r| max_index() + r.slot == mapindex).map(|r| r.m as i32).unwrap_or(-1)
}

// ---------------------------------------------------------------------------------------------- pc.cpp

/// pc.cpp pc_reg_received: the Taekwon mission (for the Taekwon line only: "better check for class rather than skill
/// to prevent skill resets from unsetting this") and the Star Gladiator maps and monsters ("SG map and mob read").
pub fn pc_reg_received(c: &Ctx, aid: u32) {
    let class_ = crate::status::data::sd_get(c, aid).map(|s| s.class_).unwrap_or(0);
    let mut s = fresh(aid);
    if class_ & MAPID_FIRSTMASK == MAPID_TAEKWON {
        s.mission_mobid = readglobalreg(c, aid, TKMISSIONID_VAR) as i16;
        s.mission_count = readglobalreg(c, aid, TKMISSIONCOUNT_VAR) as u8;
    }
    for i in 0..MAX_PC_FEELHATE {
        let j = readglobalreg(c, aid, SG_FEEL_VARS[i]) as u16;
        if j != 0 {
            s.feel_index[i] = j;
            s.feel_m[i] = map_mapindex2mapid(c, j);
        } else {
            s.feel_index[i] = 0;
            s.feel_m[i] = -1;
        }
        s.hate_mob[i] = (readglobalreg(c, aid, SG_HATE_VARS[i]) as i16).wrapping_sub(1);
    }
    put(c, &s);
}

/// pc.cpp pc_resetfeel's session part: `feel_map[i].m = -1; feel_map[i].index = 0` (skilltree.rs writes the
/// registers).
pub fn resetfeel(c: &Ctx, aid: u32, i: usize) {
    with(c, aid, |s| {
        s.feel_m[i] = -1;
        s.feel_index[i] = 0;
    });
}

/// pc.cpp pc_resethate's session part: `hate_mob[i] = -1`.
pub fn resethate(c: &Ctx, aid: u32, i: usize) {
    with(c, aid, |s| s.hate_mob[i] = -1);
}

/// pc.cpp pc_set_hate_mob: memorises `bl`'s class as the Hatred target of `pos` (0 sun: small, 1 moon: medium with
/// at least 6000 MaxHP, 2 star: large with at least 20000 MaxHP; players always qualify). A set target cannot change
/// (its info is shown again).
pub fn pc_set_hate_mob(c: &Ctx, sd: &Unit, pos: i32, bl: &Unit) -> bool {
    if !(0..=2).contains(&pos) {
        return false;
    }
    let p = pos as usize;
    let current = hate_mob(c, sd.id, p);
    if current != -1 {
        //Can't change hate targets.
        clif_hate_info(c, sd, p as u8, current, 0); //Display current
        return false;
    }
    let class_ = crate::battle::bl::class_of(c, bl);
    if !crate::db::jobs::pcdb_checkid(class_) {
        let st = crate::status::status_get_status_data(c, bl);
        let max_hp = st.max_hp as u32;
        if (pos == 1 && max_hp < 6000) || (pos == 2 && max_hp < 20000) {
            return false;
        }
        if pos != st.size as i32 {
            return false; //Wrong size
        }
    }
    with(c, sd.id, |s| s.hate_mob[p] = class_ as i16);
    setglobalreg(c, sd.id, SG_HATE_VARS[p], class_ as i64 + 1);
    clif_hate_info(c, sd, p as u8, class_, 1);
    true
}

/// pc.cpp pc_resetstate's mission part (bugreport:2200): a Taekwon mission is dropped with the stat reset.
pub fn pc_resetstate_mission(c: &Ctx, aid: u32) {
    if mission_mobid(c, aid) != 0 {
        with(c, aid, |s| {
            s.mission_mobid = 0;
            s.mission_count = 0;
        });
        setglobalreg(c, aid, TKMISSIONID_VAR, 0);
    }
}

/// skills/taekwon/mission.cpp SkillMission's session part: a new mission target.
pub fn set_mission(c: &Ctx, aid: u32, mobid: i32) {
    with(c, aid, |s| {
        s.mission_mobid = mobid as i16;
        s.mission_count = 0;
    });
    setglobalreg(c, aid, TKMISSIONID_VAR, mobid as i64);
}

// ---------------------------------------------------------------------------------------------- mob.cpp

/// mob.cpp mob_dead's Star Gladiator experience bonus: SC_MIRACLE makes every monster a star target, else the first
/// Hatred target of this class (on its day, or any day with allow_skill_without_day); with the matching
/// SG_*_BLESS the bonus is 20% (star) or 10% per level. `class_` is status_get_class(md).
pub fn mob_dead_bless_bonus(c: &Ctx, aid: u32, class_: i32) -> i32 {
    let i = if crate::status::sc::has_sce(c, aid, crate::status::consts::SC_MIRACLE) {
        2 //All mobs are Star Targets
    } else {
        let hate = hate_mobs(c, aid);
        (0..MAX_PC_FEELHATE).find(|&i| class_ == hate[i] && (crate::mob::conf::allow_skill_without_day() || crate::skill::condition::sg_day_func(c, i))).unwrap_or(MAX_PC_FEELHATE)
    };
    if i < MAX_PC_FEELHATE {
        let temp = crate::skill::checkskill(c, aid, SG_BLESS[i]) as i32;
        if temp != 0 {
            return (if i == 2 { 20 } else { 10 }) * temp;
        }
    }
    0
}

/// mob.cpp mob_dead's TK_MISSION [Skotlex]: a kill of the mission target (or, with taekwon_mission_mobname, of a
/// monster sharing its goblin race group / its name) counts; the 100th kill gives fame and a new target
/// (mob_get_random_id(MOBG_BRANCH_OF_DEAD_TREE, …) as rAthena does, not the Taekwon mission group).
pub fn mob_dead_mission(c: &Ctx, aid: u32, mob_id: i32) {
    use crate::db::mobs::*;
    let s = get(c, aid);
    let mission_mdb = mob_db().find(s.mission_mobid as u32);
    let mob = mob_db().find(mob_id as u32);
    let hit = s.mission_mobid as i32 == mob_id
        || mission_mdb.is_some_and(|m| {
            let tmn = crate::mob::conf::taekwon_mission_mobname();
            (tmn == 1 && mob.is_some_and(|x| x.race2.contains(&crate::mob::RC2_GOBLIN)) && m.race2.contains(&crate::mob::RC2_GOBLIN))
                || (tmn == 2 && mob.is_some_and(|x| x.jname == m.jname))
        });
    if !hit {
        return;
    }
    //TK_MISSION [Skotlex]
    let count = s.mission_count.wrapping_add(1);
    with(c, aid, |s| s.mission_count = count);
    if count >= 100 {
        let base_level = crate::pc::character_of(c, aid).map(|ch| ch.base_level as i32).unwrap_or(1);
        let temp = crate::mob::spawn::get_random_id(c, MOBG_BRANCH_OF_DEAD_TREE, RMF_CHECK_MOB_LV | RMF_MOB_NOT_BOSS | RMF_MOB_NOT_SPAWN, base_level);
        if temp != 0 {
            crate::fame::pc_addfame(c, aid, crate::mob::conf::fame_taekwon_mission());
            with(c, aid, |s| s.mission_mobid = temp as i16);
            setglobalreg(c, aid, TKMISSIONID_VAR, temp as i64);
            with(c, aid, |s| s.mission_count = 0);
            if let Some(sd) = crate::unit::get(c, aid) {
                clif_mission_info(c, &sd, temp, 0);
            }
        }
    }
    setglobalreg(c, aid, TKMISSIONCOUNT_VAR, mission_count(c, aid) as i64);
}

// ---------------------------------------------------------------------------------------------- clif.cpp

/// clif.cpp clif_starskill: ZC_STARSKILL (0x20e) <map name>.24B <monster id>.L <star>.B <result>.B to the player.
/// result: 0 / 1 place registered / info, 10 / 11 target registered / info, 20 mission target, 30 reset.
pub fn clif_starskill(c: &Ctx, sd: &Unit, mapname: &[u8], monster_id: i32, star: u8, result: u8) {
    let mut w = Vec::with_capacity(32);
    w.extend_from_slice(&0x20eu16.to_le_bytes());
    // safestrncpy(WFIFOCP(fd,2), mapname, NAME_LENGTH)
    let k = mapname.iter().position(|&b| b == 0).unwrap_or(mapname.len()).min(NAME_LENGTH - 1);
    w.extend_from_slice(&mapname[..k]);
    w.resize(2 + NAME_LENGTH, 0);
    w.extend_from_slice(&monster_id.to_le_bytes());
    w.push(star);
    w.push(result);
    clif::send(c, &w, sd, Target::SelfOnly);
}

/// clif.cpp clif_feel_info: the Feeling map of `feel_level` ("<map>.gat"; type 1 information, 0 registered).
pub fn clif_feel_info(c: &Ctx, sd: &Unit, feel_level: u8, type_: u8) {
    let name = mapindex_id2name(c, feel_index(c, sd.id, feel_level as usize));
    let mapname = crate::clan::mapindex_getmapname_ext(&name);
    clif_starskill(c, sd, &crate::net::wire::latin1(&mapname), 0, feel_level, if type_ != 0 { 1 } else { 0 });
}

/// clif.cpp clif_hate_info: the Hatred target of `hate_level` (a job name for a player class, the monster's jname;
/// type 1 register, 0 information).
pub fn clif_hate_info(c: &Ctx, sd: &Unit, hate_level: u8, class_: i32, type_: u8) {
    let result = if type_ != 0 { 10 } else { 11 };
    if crate::db::jobs::pcdb_checkid(class_) {
        clif_starskill(c, sd, &crate::net::wire::latin1(crate::db::jobs::job_name(class_)), class_, hate_level, result);
    } else if crate::db::mobs::mobdb_checkid(class_) != 0 {
        let jname = crate::db::mobs::mob_db().find(class_ as u32).map(|m| m.jname.clone()).unwrap_or_default();
        clif_starskill(c, sd, &crate::net::wire::latin1(&jname), class_, hate_level, result);
    } else {
        let char_id = crate::pc::character_of(c, sd.id).map(|ch| ch.id).unwrap_or(0);
        log::warn!("clif_hate_info: Received invalid class {class_} for this packet (char_id={char_id}, hate_level={hate_level}, type={type_}).");
    }
}

/// clif.cpp clif_mission_info: the Taekwon mission target and the kills so far.
pub fn clif_mission_info(c: &Ctx, sd: &Unit, mob_id: i32, progress: u8) {
    let jname = crate::db::mobs::mob_db().find(mob_id as u32).map(|m| m.jname.clone()).unwrap_or_default();
    clif_starskill(c, sd, &crate::net::wire::latin1(&jname), mob_id, progress, 20);
}

/// clif.cpp clif_feel_hate_reset: `clif_starskill(sd, "", 0, 0, 30)`.
pub fn clif_feel_hate_reset(c: &Ctx, sd: &Unit) {
    clif_starskill(c, sd, b"", 0, 0, 30);
}

/// clif.cpp clif_feel_req: ZC_STARPLACE (0x253) <which>.B, the Feeling confirmation prompt; the answer is
/// clif_parse_FeelSaveOk (sd->menuskill_id = SG_FEEL, menuskill_val = skill_lv).
pub fn clif_feel_req(c: &Ctx, sd: &Unit, skill_lv: u16) {
    let w = [0x53u8, 0x02, skill_lv.wrapping_sub(1) as u8];
    clif::send(c, &w, sd, Target::SelfOnly);
    let (_, _, val2) = crate::itemui::menu(c, sd.id);
    crate::itemui::set_menu(c, sd.id, SG_FEEL as i32, skill_lv as i32, val2);
}

/// clif.cpp clif_parse_FeelSaveOk (CZ_AGREE_STARPLACE 0x254): the player's current map becomes the Feeling map of
/// the level the prompt was opened with (the packet's own choice is unused, "for security purpose").
pub fn clif_parse_feel_save_ok(c: &Ctx, aid: u32) {
    let (menuskill_id, menuskill_val, _) = crate::itemui::menu(c, aid);
    if menuskill_id != SG_FEEL as i32 {
        return;
    }
    let i = menuskill_val - 1;
    if i < 0 || i >= MAX_PC_FEELHATE as i32 {
        return; //Bug?
    }
    let Some(sd) = crate::unit::get(c, aid) else { return };
    let i = i as usize;
    let index = map_id2index(c, sd.map);
    with(c, aid, |s| {
        s.feel_index[i] = index;
        s.feel_m[i] = sd.map as i32;
    });
    setglobalreg(c, aid, SG_FEEL_VARS[i], index as i64);
    clif_feel_info(c, &sd, i as u8, 0);
    crate::itemui::clif_menuskill_clear(c, aid);
}

/// clif.cpp clif_parse_NoviceDoriDori (CZ_DORIDORI 0x1e7): the /doridori recovery bonus (status_natural_heal) for a
/// resting Taekwon / Star Gladiator / Soul Linker and for any Super Novice.
pub fn clif_parse_novice_doridori(c: &Ctx, aid: u32) {
    let Some(st) = crate::status::data::sd_get(c, aid) else { return };
    if st.doridori {
        return;
    }
    let set = match st.class_ & MAPID_SECONDMASK {
        MAPID_SOUL_LINKER | MAPID_STAR_GLADIATOR | MAPID_TAEKWON => st.rest,
        MAPID_SUPER_NOVICE | MAPID_SUPER_BABY | MAPID_SUPER_NOVICE_E | MAPID_SUPER_BABY_E => true,
        _ => false,
    };
    if set {
        crate::status::data::with_sd(c, aid, |s| s.doridori = true);
    }
}

/// net/mod.rs dispatch: the requests this file serves.
pub fn parse(c: &Ctx, aid: u32, func: &str, _pkt: &[u8]) -> bool {
    match func {
        "clif_parse_FeelSaveOk" => clif_parse_feel_save_ok(c, aid),
        "clif_parse_NoviceDoriDori" => clif_parse_novice_doridori(c, aid),
        _ => return false,
    }
    true
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn mapindex_table() {
        // db/map_index.txt starts with "alb_ship 1", then alb2trea (2)
        let names = &mapindex_db().names;
        assert_eq!(names[0], "");
        assert_eq!(names[1], "alb_ship");
        assert_eq!(names[2], "alb2trea");
        assert!(names.iter().filter(|n| n.as_str() == "prontera").count() == 1);
        assert_eq!(crate::db::misc::mapindex_getmapname("prontera.gat"), "prontera");
    }
}
