//! pc.cpp's skill tree and job progression (rAthena e985006, RENEWAL): pc_calc_skilltree (with
//! pc_calc_skilltree_normalize_job(_sub), pc_calc_skillpoint, pc_grant_allskills), pc_check_skilltree, pc_skill,
//! pc_skillup, pc_allskillup, pc_resetskill, pc_resetlvl, pc_jobchange (with pc_setoption, status_set_viewdata for a
//! player, status_db.changeSkillTree, pc_checkallowskill, pc_resetfeel / pc_resethate, pc_skill_plagiarism_reset),
//! pc_validate_skill, pc_clean_skilltree and the skill parts of pc_reg_received / map_quit.
//!
//! **How sd->status.skill[] is kept.** rAthena holds MAX_SKILL slots `{ id, lv, flag }` indexed by skill_get_index.
//! A slot's `id` is set when the skill is in the character's current tree (the client's skill window lists every such
//! slot, level 0 included); `lv` / `flag` are the learned level and its kind (permanent, item-granted, plagiarised …)
//! and survive while `id` is 0 (a learned skill the current tree hides). Here a slot is split over two tables:
//! - `char_skill` (skill.rs; rAthena's `skill` table, also the live store): one row per slot whose `lv` or `flag` is
//!   not zero;
//! - [`PcSkilltree::known`] (runtime, one row per character in the world): the skills whose slot has its `id` set.
//! [`Skills`] loads both into the slot array, the functions below work on it exactly as rAthena works on the array,
//! and [`Skills::save`] writes the difference back.
//!
//! **Saving.** rAthena's char server stores only some slots (char.cpp mmo_char_tosql: `id != 0`, not TEMPORARY, level
//! above 0, the original level of a replaced one), after map_quit ran pc_clean_skilltree. The module has no separate
//! save: [`map_quit`] applies pc_clean_skilltree and that filter to the rows when the character leaves, and
//! [`pc_validate_skill`] applies the filter again at the next login (char_mmo_char_fromsql + pc_validate_skill), which
//! also covers a character that never left cleanly.

use std::collections::BTreeMap;
use std::sync::OnceLock;

use spacetimedb::{table, Table};

use crate::character::Character;
use crate::db::jobs::*;
use crate::db::skills::{self as sk, skill_db, skill_get_inf, skill_get_inf2, skill_get_max, skill_tree_get_max};
use crate::skill::{char_skill, CharSkill};
use crate::status::consts::*;
use crate::party::party_data;
use crate::pc::pc;
use crate::status::data;
use crate::status::defs::*;
use crate::world::Ctx;

// ---------------------------------------------------------------------------------------------- configuration

/// conf/battle/player.conf `player_skillfree: no` (battle_config.skillfree).
pub fn skillfree() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::PLAYER_SKILLFREE) != 0
}
/// conf/battle/player.conf `player_skillup_limit: yes` (battle_config.skillup_limit).
pub fn skillup_limit() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::PLAYER_SKILLUP_LIMIT) != 0
}
/// conf/battle/player.conf `quest_skill_learn: no`.
pub fn quest_skill_learn() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::QUEST_SKILL_LEARN) != 0
}
/// conf/battle/player.conf `quest_skill_reset: no`.
pub fn quest_skill_reset() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::QUEST_SKILL_RESET) != 0
}
/// conf/battle/player.conf `taekwon_ranker_min_lv: 90`.
pub fn taekwon_ranker_min_lv() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::TAEKWON_RANKER_MIN_LV) as u16
}
/// conf/battle/skill.conf `skillrange_from_weapon: 0` (battle_config.use_weapon_skill_range, BL_NUL).
pub fn use_weapon_skill_range() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::SKILLRANGE_FROM_WEAPON) as u16
}
/// conf/battle/skill.conf `dancing_weaponswitch_fix: yes`.
pub fn dancing_weaponswitch_fix() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::DANCING_WEAPONSWITCH_FIX) != 0
}
/// conf/battle/misc.conf `wedding_ignorepalette`, `xmas_ignorepalette`, `summer_ignorepalette`, `hanbok_ignorepalette`,
/// `oktoberfest_ignorepalette`: all `no`.
pub const IGNORE_PALETTE: bool = false;

/// pc.hpp JOBCHANGE2ND_VAR / JOBCHANGE3RD_VAR / JOBCHANGE4TH_VAR.
pub const JOBCHANGE2ND_VAR: &str = "jobchange_level";
pub const JOBCHANGE3RD_VAR: &str = "jobchange_level_3rd";
pub const JOBCHANGE4TH_VAR: &str = "jobchange_level_4th";
/// skill.hpp SKILL_VAR_PLAGIARISM / _LV, SKILL_VAR_REPRODUCE / _LV.
pub const SKILL_VAR_PLAGIARISM: &str = "CLONE_SKILL";
pub const SKILL_VAR_PLAGIARISM_LV: &str = "CLONE_SKILL_LV";
pub const SKILL_VAR_REPRODUCE: &str = "REPRODUCE_SKILL";
pub const SKILL_VAR_REPRODUCE_LV: &str = "REPRODUCE_SKILL_LV";
/// pc.cpp sg_info[].feel_var / hate_var (Star Gladiator feel maps and hated monsters).
const SG_FEEL_VARS: [&str; 3] = ["PC_FEEL_SUN", "PC_FEEL_MOON", "PC_FEEL_STAR"];
const SG_HATE_VARS: [&str; 3] = ["PC_HATE_MOB_SUN", "PC_HATE_MOB_MOON", "PC_HATE_MOB_STAR"];

// ---------------------------------------------------------------------------------------------- the slot array

/// mmo.hpp struct s_skill: one slot of sd->status.skill[] (the slot's index is skill_get_index of its skill).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct Slot {
    pub id: u16,
    pub lv: u8,
    pub flag: u8,
}

/// The skill-tree part of map_session_data that is not saved with the character.
#[table(accessor = pc_skilltree)]
#[derive(Clone, Debug, Default, PartialEq)]
pub struct PcSkilltree {
    #[primary_key]
    pub account_id: u32,
    #[unique]
    pub char_id: u32,
    /// The skills whose slot has its id set (sd->status.skill[idx].id != 0), in skill-index order.
    pub known: Vec<u16>,
    /// sd->change_level_2nd / _3rd / _4th (the job level at the job change; JOBCHANGE*_VAR registers).
    pub change_level_2nd: u8,
    pub change_level_3rd: u8,
    pub change_level_4th: u8,
    /// sd->cloneskill_idx / sd->reproduceskill_idx (the slot of the plagiarised / reproduced skill, 0 = none).
    pub cloneskill_idx: u16,
    pub reproduceskill_idx: u16,
}

/// skill index → skill id (the inverse of skill_get_index; 0 for an unused index).
fn idx2skill() -> &'static [u16] {
    static T: OnceLock<Vec<u16>> = OnceLock::new();
    T.get_or_init(|| {
        let mut v = vec![0u16; sk::MAX_SKILL + 1];
        for s in skill_db().iter() {
            let i = s.index as usize;
            if i > 0 && i < v.len() && v[i] == 0 {
                v[i] = s.nameid;
            }
        }
        v
    })
}

/// The skill whose slot is `idx`.
pub fn idx_skill(idx: u16) -> u16 {
    idx2skill().get(idx as usize).copied().unwrap_or(0)
}

/// skill_get_index without the "undefined" error (rows read back from the tables).
fn index_silent(skill_id: u16) -> u16 {
    skill_db().get_index(skill_id, true)
}

/// A character's sd->status.skill[] (slots that are all zero are absent) plus the runtime row.
#[derive(Clone, Debug)]
pub struct Skills {
    pub aid: u32,
    pub char_id: u32,
    pub slots: BTreeMap<u16, Slot>,
    pub meta: PcSkilltree,
    orig_rows: Vec<CharSkill>,
    orig_meta: Option<PcSkilltree>,
}

impl Skills {
    /// The slots of an online character. Without a runtime row (the character has not been through pc_validate_skill
    /// yet) every learned row has its id set, as after the char server's load.
    pub fn load(c: &Ctx, aid: u32) -> Option<Skills> {
        let p = crate::pc::get(c, aid)?;
        Some(Self::load_char(c, aid, p.char_id))
    }

    fn load_char(c: &Ctx, aid: u32, char_id: u32) -> Skills {
        let rows: Vec<CharSkill> = c.db().char_skill().by_char().filter(char_id).collect();
        let meta_row = c.db().pc_skilltree().account_id().find(aid).filter(|m| m.char_id == char_id);
        let mut slots: BTreeMap<u16, Slot> = BTreeMap::new();
        for r in &rows {
            let idx = index_silent(r.skill_id);
            if idx == 0 {
                continue; // an invalid row (pc_validate_skill removes it at login)
            }
            let e = slots.entry(idx).or_default();
            e.lv = r.lv;
            e.flag = r.flag;
            if meta_row.is_none() && r.lv > 0 {
                e.id = r.skill_id;
            }
        }
        if let Some(m) = &meta_row {
            for &id in &m.known {
                let idx = index_silent(id);
                if idx != 0 {
                    slots.entry(idx).or_default().id = id;
                }
            }
        }
        let meta = meta_row.clone().unwrap_or(PcSkilltree { account_id: aid, char_id, ..Default::default() });
        Skills { aid, char_id, slots, meta, orig_rows: rows, orig_meta: meta_row }
    }

    /// sd->status.skill[idx].
    pub fn at(&self, idx: u16) -> Slot {
        self.slots.get(&idx).copied().unwrap_or_default()
    }

    /// The slot of a skill (a zero slot for an invalid skill).
    pub fn of(&self, skill_id: u16) -> Slot {
        self.at(index_silent(skill_id))
    }

    pub fn put(&mut self, idx: u16, s: Slot) {
        if idx == 0 {
            return;
        }
        if s == Slot::default() {
            self.slots.remove(&idx);
        } else {
            self.slots.insert(idx, s);
        }
    }

    /// pc.cpp pc_checkskill on this array (guild skills are not in it: see [`crate::skill::checkskill`]).
    pub fn checkskill(&self, skill_id: u16) -> u8 {
        let idx = index_silent(skill_id);
        if idx == 0 {
            return 0;
        }
        let s = self.at(idx);
        if s.id == skill_id {
            s.lv
        } else {
            0
        }
    }

    /// The slots in index order (what memcmp / the MAX_SKILL loops see).
    pub fn snapshot(&self) -> Vec<(u16, Slot)> {
        self.slots.iter().map(|(&i, &s)| (i, s)).collect()
    }

    /// Writes the array back: `char_skill` rows for slots with a level or a flag, the runtime row with the ids.
    pub fn save(&mut self, c: &Ctx) {
        let mut desired: BTreeMap<u16, (u8, u8)> = BTreeMap::new();
        for (&idx, s) in &self.slots {
            if s.lv != 0 || s.flag != SKILL_FLAG_PERMANENT {
                desired.insert(idx_skill(idx), (s.lv, s.flag));
            }
        }
        let mut done: Vec<u16> = Vec::new();
        let mut kept: Vec<CharSkill> = Vec::new();
        for r in &self.orig_rows {
            if index_silent(r.skill_id) == 0 {
                kept.push(r.clone());
                continue; // invalid rows are left to pc_validate_skill
            }
            match desired.get(&r.skill_id) {
                Some(&(lv, flag)) if !done.contains(&r.skill_id) => {
                    done.push(r.skill_id);
                    if r.lv != lv || r.flag != flag {
                        let row = CharSkill { lv, flag, ..r.clone() };
                        c.db().char_skill().id().update(row.clone());
                        kept.push(row);
                    } else {
                        kept.push(r.clone());
                    }
                }
                _ => {
                    c.db().char_skill().id().delete(r.id);
                }
            }
        }
        for (&skill_id, &(lv, flag)) in &desired {
            if !done.contains(&skill_id) {
                kept.push(c.db().char_skill().insert(CharSkill { id: 0, char_id: self.char_id, skill_id, lv, flag }));
            }
        }
        self.orig_rows = kept;
        self.meta.known = self.slots.values().filter(|s| s.id != 0).map(|s| s.id).collect();
        if self.orig_meta.as_ref() != Some(&self.meta) {
            if c.db().pc_skilltree().account_id().find(self.aid).is_some() {
                c.db().pc_skilltree().account_id().update(self.meta.clone());
            } else {
                if let Some(old) = c.db().pc_skilltree().char_id().find(self.char_id) {
                    c.db().pc_skilltree().account_id().delete(old.account_id);
                }
                c.db().pc_skilltree().insert(self.meta.clone());
            }
            self.orig_meta = Some(self.meta.clone());
        }
    }
}

/// The level of a skill the character has (pc_checkskill for non-guild skills), read straight from the tables.
pub fn checkskill_rows(c: &Ctx, aid: u32, char_id: u32, skill_id: u16) -> u8 {
    let lv = c.db().char_skill().by_char().filter(char_id).find(|s| s.skill_id == skill_id).map(|s| s.lv).unwrap_or(0);
    match c.db().pc_skilltree().account_id().find(aid).filter(|m| m.char_id == char_id) {
        Some(m) => {
            if m.known.contains(&skill_id) {
                lv
            } else {
                0
            }
        }
        None => lv, // loaded skills have their id set
    }
}

// ---------------------------------------------------------------------------------------------- helpers

fn ch_of(c: &Ctx, aid: u32) -> Option<Character> {
    crate::pc::character_of(c, aid)
}

fn put_char(c: &Ctx, ch: &Character) {
    crate::status::put_char(c, ch);
}

fn upd(c: &Ctx, aid: u32, sp: i32) {
    crate::status::clif::updatestatus(c, aid, sp);
}

fn has_perm(c: &Ctx, aid: u32, perm: u32) -> bool {
    crate::pc::has_permission(c, aid, perm)
}

/// pc_readglobalreg: a permanent character variable.
pub fn read_char_reg(c: &Ctx, aid: u32, name: &str) -> i64 {
    use crate::script::vm::{CodeKey, Rt, State};
    let Some(p) = crate::pc::get(c, aid) else { return 0 };
    let rt = Rt::new(c);
    let st = State::new(CodeKey::Func(String::new()), std::rc::Rc::new(Default::default()), 0, aid, 0);
    crate::script::reg::store_get_num(&rt, &st, &crate::script::reg::Store::Char(p.char_id), &name.to_ascii_lowercase(), 0)
}

/// pc_setglobalreg.
pub fn set_char_reg(c: &Ctx, aid: u32, name: &str, v: i64) {
    crate::status::write_char_reg_num(c, aid, name, v);
}

/// pc.hpp pc_is_taekwon_ranker.
pub fn pc_is_taekwon_ranker(c: &Ctx, class_: u64, ch: &Character) -> bool {
    (class_ & MAPID_SECONDMASK) == MAPID_TAEKWON && ch.base_level >= taekwon_ranker_min_lv() && crate::status::calc::pc_famerank(c, ch.id, MAPID_TAEKWON) != 0
}

/// skill.hpp skill_ischangesex.
pub fn skill_ischangesex(id: u16) -> bool {
    (sk::BD_ADAPTATION..=sk::DC_SERVICEFORYOU).contains(&id)
        || (sk::CG_ARROWVULCAN..=sk::CG_MARIONETTE).contains(&id)
        || (sk::CG_LONGINGFREEDOM..=sk::CG_TAROTCARD).contains(&id)
        || (sk::WA_SWING_DANCE..=sk::WM_UNLIMITED_HUMMING_VOICE).contains(&id)
}

fn summoner_passive(c: &Ctx, aid: u32) -> bool {
    use crate::status::calc::{pc_checkskill_summoner, who_of};
    who_of(c, aid).map(|w| pc_checkskill_summoner(&w, SUMMONER_POWER_LAND) >= 20 || pc_checkskill_summoner(&w, SUMMONER_POWER_SEA) >= 20).unwrap_or(false)
}

/// skill.cpp skill_get_range2 for a player (the BL_PC branches; `skillrange_from_weapon` is off).
pub fn skill_get_range2_pc(c: &Ctx, aid: u32, skill_id: u16, skill_lv: u16, is_server: bool) -> i32 {
    let mut range = sk::skill_get_range(skill_id, skill_lv);
    if range < 0 {
        // battle_config.use_weapon_skill_range & BL_PC: status_get_range (off)
        let _ = use_weapon_skill_range();
        range *= -1;
    }
    if is_server && range > 14 {
        range = 14; // Server-sided base range can't be above 14
    }
    let Some(skill) = skill_db().find(skill_id) else { return range }; // (rAthena dereferences skill_db.find)
    let chk = |id| crate::skill::checkskill(c, aid, id) as i32;
    if skill.has_inf2(sk::INF2_ALTERRANGEVULTURE) || skill.has_inf2(sk::INF2_ALTERRANGESNAKEEYE) {
        if skill.has_inf2(sk::INF2_ALTERRANGEVULTURE) {
            range += chk(sk::AC_VULTURE);
        }
        // added to allow GS skills to be effected by the range of Snake Eyes [Reddozen]
        if skill.has_inf2(sk::INF2_ALTERRANGESNAKEEYE) {
            range += chk(sk::GS_SNAKEEYE);
        }
    }
    if skill.has_inf2(sk::INF2_ALTERRANGESHADOWJUMP) || skill.has_inf2(sk::INF2_ALTERRANGERADIUS) || skill.has_inf2(sk::INF2_ALTERRANGERESEARCHTRAP) {
        if skill.has_inf2(sk::INF2_ALTERRANGESHADOWJUMP) {
            range = sk::skill_get_range(sk::NJ_SHADOWJUMP, chk(sk::NJ_SHADOWJUMP) as u16);
        }
        if skill.has_inf2(sk::INF2_ALTERRANGERADIUS) {
            range += chk(sk::WL_RADIUS);
        }
        if skill.has_inf2(sk::INF2_ALTERRANGERESEARCHTRAP) {
            let rt_range = [0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5];
            range += rt_range.get(chk(sk::RA_RESEARCHTRAP) as usize).copied().unwrap_or(0);
        }
    }
    range
}

// ---------------------------------------------------------------------------------------------- pc_calc_skilltree

/// pc.cpp pc_calc_skillpoint: the skill points spent (uint16, wrapping as rAthena's counter does).
fn pc_calc_skillpoint(s: &Skills) -> i32 {
    let mut skill_point: u16 = 0;
    for (&idx, sl) in &s.slots {
        if idx < 1 || sl.id == 0 || sl.lv == 0 {
            continue;
        }
        let Some(skill) = skill_db().find(sl.id) else { continue };
        // Do not count wedding/link skills. [Skotlex]
        if (!skill.has_inf2(sk::INF2_ISQUEST) || quest_skill_learn()) && (!skill.has_inf2(sk::INF2_ISWEDDING) || skill.has_inf2(sk::INF2_ISSPIRIT)) {
            if sl.flag == SKILL_FLAG_PERMANENT {
                skill_point = skill_point.wrapping_add(sl.lv as u16);
            } else if sl.flag >= SKILL_FLAG_REPLACED_LV_0 {
                skill_point = skill_point.wrapping_add((sl.flag - SKILL_FLAG_REPLACED_LV_0) as u16);
            }
        }
    }
    skill_point as i32
}

/// The job db's MaxJobLevel of a class (rAthena dereferences job_db.find; 0 here when missing).
fn max_job_level(class_: i32) -> i32 {
    job_db().find(class_ as u16).map(|j| j.max_job_level as i32).unwrap_or(0)
}

/// pc.cpp pc_calc_skilltree_normalize_job_sub: the class whose tree the spent skill points reach.
fn normalize_job_sub(c: &Ctx, aid: u32, s: &mut Skills, class_: u64, sex: i32) -> u64 {
    let mut skill_point = pc_calc_skillpoint(s);
    if (class_ & MAPID_FIRSTMASK) != MAPID_SUMMONER {
        // Novice's skill points for basic skill.
        let novice_skills = max_job_level(JOB_NOVICE) - 1;
        if skill_point < novice_skills {
            return MAPID_NOVICE;
        }
        skill_point -= novice_skills;
    }
    // 1st Class Job LV Check
    if class_ & JOBL_2 != 0 && (class_ & MAPID_SECONDMASK) != MAPID_SUPER_NOVICE {
        let mapid_1st = class_ & MAPID_FIRSTMASK;
        let class_1st = pc_mapid2jobid(mapid_1st, sex);
        if s.meta.change_level_2nd == 0 {
            s.meta.change_level_2nd = if class_1st >= JOB_NOVICE { max_job_level(class_1st) as u8 } else { 0 };
            set_char_reg(c, aid, JOBCHANGE2ND_VAR, s.meta.change_level_2nd as i64);
        }
        if class_1st > 0 && skill_point < s.meta.change_level_2nd as i32 - 1 {
            return mapid_1st;
        }
        skill_point -= s.meta.change_level_2nd as i32 - 1;
    }
    // 2nd Class Job LV Check
    if class_ & JOBL_THIRD != 0 {
        let mapid_2nd = class_ & MAPID_SECONDMASK;
        let class_2nd = pc_mapid2jobid(mapid_2nd, sex);
        if s.meta.change_level_3rd == 0 {
            s.meta.change_level_3rd = if class_2nd >= JOB_NOVICE { max_job_level(class_2nd) as u8 } else { 0 };
            set_char_reg(c, aid, JOBCHANGE3RD_VAR, s.meta.change_level_3rd as i64);
        }
        if class_2nd > 0 && skill_point < s.meta.change_level_3rd as i32 - 1 {
            return mapid_2nd;
        }
        skill_point -= s.meta.change_level_3rd as i32 - 1;
    }
    // 3rd Class Job LV Check
    if class_ & JOBL_FOURTH != 0 {
        let mapid_3rd = class_ & MAPID_THIRDMASK;
        let class_3rd = pc_mapid2jobid(mapid_3rd, sex);
        if s.meta.change_level_4th == 0 {
            s.meta.change_level_4th = if class_3rd >= JOB_NOVICE { max_job_level(class_3rd) as u8 } else { 0 };
            set_char_reg(c, aid, JOBCHANGE4TH_VAR, s.meta.change_level_4th as i64);
        }
        if class_3rd > 0 && skill_point < s.meta.change_level_4th as i32 - 1 {
            return mapid_3rd;
        }
        let _ = skill_point - (s.meta.change_level_4th as i32 - 1);
    }
    class_
}

/// pc.cpp pc_calc_skilltree_normalize_job: the MAPID whose skill tree the character may use (player_skillup_limit).
pub fn normalize_job(c: &Ctx, aid: u32, s: &mut Skills, class_: u64, sex: i32) -> u64 {
    if !skillup_limit() || has_perm(c, aid, crate::rules::PC_PERM_ALL_SKILL) {
        return class_;
    }
    let mut cc = normalize_job_sub(c, aid, s, class_, sex);
    // Special Masks
    if class_ & JOBL_UPPER != 0 && pc_mapid2jobid(cc | JOBL_UPPER, sex) >= JOB_NOVICE {
        cc |= JOBL_UPPER; // Rebirth Job
    }
    if class_ & JOBL_BABY != 0 && pc_mapid2jobid(cc | JOBL_BABY, sex) >= JOB_NOVICE {
        cc |= JOBL_BABY; // Baby Job
    }
    // Primary 3rd and 4th Jobs Can Access Trans Job Skill Trees.
    if (pc_is_primary_third(class_) || pc_is_primary_fourth(class_)) && !pc_is_primary_fourth(cc) && pc_mapid2jobid(cc | JOBL_UPPER, sex) >= JOB_NOVICE {
        cc |= JOBL_UPPER;
    }
    cc
}

/// pc.cpp pc_calc_skilltree_normalize_job for a character in the world (@skilltree).
pub fn normalize_job_of(c: &Ctx, aid: u32) -> Option<u64> {
    let mut s = Skills::load(c, aid)?;
    let sd = data::sd_get(c, aid)?;
    let ch = ch_of(c, aid)?;
    let r = normalize_job(c, aid, &mut s, sd.class_, ch.sex as i32);
    s.save(c);
    Some(r)
}

/// pc.cpp pc_grant_allskills (PC_PERM_ALL_SKILL): every skill but NPC / guild skills and the dummy ones.
fn pc_grant_allskills(c: &Ctx, aid: u32, s: &mut Skills, addlv: bool) -> bool {
    if !has_perm(c, aid, crate::rules::PC_PERM_ALL_SKILL) {
        return false;
    }
    // Dummy skills must NOT be added here otherwise they'll be displayed in the skill tree and since they have no
    // icons they'll give resource errors. Get ALL skills except npc/guild ones. [Skotlex]
    // Don't add SG_DEVIL [Komurka] and MO_TRIPLEATTACK and RG_SNATCHER [ultramage]
    for skill in skill_db().iter() {
        let skill_id = skill.nameid;
        if skill_id == 0 || skill.has_inf2(sk::INF2_ISNPC) || skill.has_inf2(sk::INF2_ISGUILD) {
            continue;
        }
        if matches!(
            skill_id,
            sk::SM_SELFPROVOKE
                | sk::AB_DUPLELIGHT_MELEE
                | sk::AB_DUPLELIGHT_MAGIC
                | sk::WL_CHAINLIGHTNING_ATK
                | sk::WL_TETRAVORTEX_FIRE
                | sk::WL_TETRAVORTEX_WATER
                | sk::WL_TETRAVORTEX_WIND
                | sk::WL_TETRAVORTEX_GROUND
                | sk::WL_SUMMON_ATK_FIRE
                | sk::WL_SUMMON_ATK_WIND
                | sk::WL_SUMMON_ATK_WATER
                | sk::WL_SUMMON_ATK_GROUND
                | sk::LG_OVERBRAND_BRANDISH
                | sk::LG_OVERBRAND_PLUSATK
                | sk::WM_SEVERE_RAINSTORM_MELEE
                | sk::RL_R_TRIP_PLUSATK
                | sk::SG_DEVIL
                | sk::MO_TRIPLEATTACK
                | sk::RG_SNATCHER
        ) {
            continue;
        }
        let lv = skill_get_max(skill_id) as u8;
        if lv > 0 {
            let idx = sk::skill_get_index(skill_id);
            let mut sl = s.at(idx);
            sl.id = skill_id;
            if addlv {
                sl.lv = lv;
            }
            s.put(idx, sl);
        }
    }
    true
}

/// pc.cpp pc_calc_skilltree: which skills of the character's (normalized) tree are available, the temporary /
/// replaced levels restored, the Taekwon Ranker bonus tree and the Bard / Dancer linked skills.
pub fn pc_calc_skilltree(c: &Ctx, aid: u32) {
    let Some(mut s) = Skills::load(c, aid) else { return };
    let Some(sd) = data::sd_get(c, aid) else { return };
    let Some(ch) = ch_of(c, aid) else { return };
    let sex = ch.sex as i32;
    let job = normalize_job(c, aid, &mut s, sd.class_, sex);
    let class_ = pc_mapid2jobid(job, sex);
    if class_ == -1 {
        // Unable to normalize job??
        log::error!("pc_calc_skilltree: Unable to normalize job {}({}) for character {} ({}:{})", job_name(ch.class as i32), ch.class, ch.name, ch.account_id, ch.id);
        s.save(c);
        return;
    }
    let job_id = class_ as u16;
    let class_ = pc_class2idx(class_);
    // (the skill_db loops below only change slots that are not zero)
    for idx in s.slots.keys().copied().collect::<Vec<u16>>() {
        let skill_id = idx_skill(idx);
        let mut sl = s.at(idx);
        if sl.flag != SKILL_FLAG_PLAGIARIZED && sl.flag != SKILL_FLAG_PERM_GRANTED {
            sl.id = 0; // First clear skills.
        }
        // permanent skills that must be re-checked
        if sl.flag == SKILL_FLAG_PERM_GRANTED {
            if skill_id == 0 {
                sl = Slot::default();
            } else if skill_id == sk::NV_TRICKDEAD && (sd.class_ & MAPID_SECONDMASK) != MAPID_NOVICE {
                sl = Slot::default();
            }
        }
        s.put(idx, sl);
    }
    for idx in s.slots.keys().copied().collect::<Vec<u16>>() {
        let mut sl = s.at(idx);
        // Restore original level of skills after deleting earned skills.
        if sl.flag != SKILL_FLAG_PERMANENT && sl.flag != SKILL_FLAG_PERM_GRANTED && sl.flag != SKILL_FLAG_PLAGIARIZED {
            sl.lv = if sl.flag == SKILL_FLAG_TEMPORARY { 0 } else { sl.flag.wrapping_sub(SKILL_FLAG_REPLACED_LV_0) };
            sl.flag = SKILL_FLAG_PERMANENT;
            s.put(idx, sl);
        }
    }
    // Removes Taekwon Ranker skill bonus
    if (sd.class_ & MAPID_SECONDMASK) != MAPID_TAEKWON {
        if let Some(tree) = skill_tree_db().find(JOB_TAEKWON as u16) {
            for e in &tree.skills {
                let sk_idx = sk::skill_get_index(e.skill_id);
                if sk_idx == 0 {
                    continue;
                }
                let mut sl = s.at(sk_idx);
                if sl.flag != SKILL_FLAG_PLAGIARIZED && sl.flag != SKILL_FLAG_PERM_GRANTED {
                    if e.skill_id == sk::NV_BASIC || e.skill_id == sk::NV_FIRSTAID || e.skill_id == sk::WE_CALLBABY {
                        continue;
                    }
                    sl.id = 0;
                    s.put(sk_idx, sl);
                }
            }
        }
    }
    // Grant all skills
    pc_grant_allskills(c, aid, &mut s, false);
    let spirit = crate::status::sc::get_sce(c, aid, SC_SPIRIT);
    let tree = skill_tree_db().find(job_id).filter(|t| !t.skills.is_empty());
    if let Some(tree) = tree {
        loop {
            let mut flag = false;
            for e in &tree.skills {
                let mut fail = false;
                let skid = e.skill_id;
                let sk_idx = sk::skill_get_index(skid);
                if s.at(sk_idx).id != 0 {
                    continue; // Skill already known.
                }
                if !skillfree() {
                    // Checking required skills
                    for &(sk_need_id, need_lv) in &e.need {
                        let sk_need_idx = sk::skill_get_index(sk_need_id);
                        if sk_need_idx > 0 {
                            let ns = s.at(sk_need_idx);
                            let sk_need: u16 = if ns.id == 0 || ns.flag == SKILL_FLAG_TEMPORARY || ns.flag == SKILL_FLAG_PLAGIARIZED {
                                0 // Not learned.
                            } else if ns.flag >= SKILL_FLAG_REPLACED_LV_0 {
                                (ns.flag - SKILL_FLAG_REPLACED_LV_0) as u16 // Real learned level
                            } else {
                                s.checkskill(sk_need_id) as u16
                            };
                            if sk_need < need_lv {
                                fail = true;
                                break;
                            }
                        }
                    }
                    if (ch.base_level as u32) < e.baselv {
                        // We need to get the actual class in this case
                        let c_ = pc_class2idx(pc_mapid2jobid(sd.class_, sex));
                        if class_ == c_ || (class_ != c_ && (ch.base_level as u32) < e.baselv) {
                            fail = true; // base level requirement wasn't satisfied
                        }
                    }
                    if (ch.job_level as u32) < e.joblv {
                        // We need to get the actual class in this case
                        let c_ = pc_class2idx(pc_mapid2jobid(sd.class_, sex));
                        if class_ == c_ || (class_ != c_ && (ch.job_level as u32) < e.joblv) {
                            fail = true; // job level requirement wasn't satisfied
                        }
                    }
                }
                if !fail {
                    let Some(skill) = skill_db().find(skid) else { continue }; // (rAthena dereferences it)
                    let mut sl = s.at(sk_idx);
                    if sl.lv == 0
                        && ((skill.has_inf2(sk::INF2_ISQUEST) && !quest_skill_learn())
                            || skill.has_inf2(sk::INF2_ISWEDDING)
                            || (skill.has_inf2(sk::INF2_ISSPIRIT) && spirit.is_none()))
                    {
                        continue; // Cannot be learned via normal means. Note this check DOES allows raising already known skills.
                    }
                    sl.id = skid;
                    if skill.has_inf2(sk::INF2_ISSPIRIT) {
                        // Spirit skills cannot be learned, they will only show up on your tree when you get buffed.
                        sl.lv = 1; // need to manually specify a skill level
                        sl.flag = SKILL_FLAG_TEMPORARY; // So it is not saved, and tagged as a "bonus" skill.
                    }
                    s.put(sk_idx, sl);
                    flag = true; // skill list has changed, perform another pass
                }
            }
            if !flag {
                break;
            }
        }
    }
    if class_ > 0 && ch.skill_point == 0 && pc_is_taekwon_ranker(c, sd.class_, &ch) {
        // Taekwon Ranker Bonus Skill Tree
        // - Grant All Taekwon Tree, but only as Bonus Skills in case they drop from ranking.
        // - (c > 0) to avoid grant Novice Skill Tree in case of Skill Reset (need more logic)
        // - (sd->status.skill_point == 0) to wait until all skill points are assigned to avoid problems with Job
        //   Change quest.
        if let Some(tree) = skill_tree_db().find(job_id) {
            for e in &tree.skills {
                let skid = e.skill_id;
                let sk_idx = sk::skill_get_index(skid);
                if sk_idx == 0 {
                    continue;
                }
                if skill_get_inf2(skid, &[sk::INF2_ISQUEST, sk::INF2_ISWEDDING]) {
                    continue; // Do not include Quest/Wedding skills.
                }
                let mut sl = s.at(sk_idx);
                if sl.id == 0 {
                    sl.id = skid;
                    sl.flag = SKILL_FLAG_TEMPORARY; // So it is not saved, and tagged as a "bonus" skill.
                } else if skid != sk::NV_BASIC {
                    sl.flag = SKILL_FLAG_REPLACED_LV_0.wrapping_add(sl.lv); // Remember original level
                }
                sl.lv = skill_tree_get_max(skid, ch.class as i32) as u8;
                s.put(sk_idx, sl);
            }
        }
    }
    s.save(c);
    // Enable Bard/Dancer spirit linked skills.
    if let Some(sce) = spirit {
        if sce.val2 == sk::SL_BARDDANCER as i32 {
            let linked_skills: [[u16; 2]; 4] = [
                [sk::BA_WHISTLE, sk::DC_HUMMING],
                [sk::BA_ASSASSINCROSS, sk::DC_DONTFORGETME],
                [sk::BA_POEMBRAGI, sk::DC_FORTUNEKISS],
                [sk::BA_APPLEIDUN, sk::DC_SERVICEFORYOU],
            ];
            let sexb = (ch.sex != 0) as usize;
            for skill in linked_skills {
                if crate::skill::checkskill(c, aid, skill[1 - sexb]) < 10 {
                    continue;
                }
                // Tag it as a non-savable, non-uppable, bonus skill
                pc_skill(c, aid, skill[sexb], 10, ADDSKILL_TEMP);
            }
        }
    }
}

/// pc.cpp pc_check_skilltree: checks if you can learn a new skill after having leveled up a skill.
fn pc_check_skilltree(c: &Ctx, aid: u32) {
    if skillfree() {
        return; // Function serves no purpose if this is set
    }
    let Some(mut s) = Skills::load(c, aid) else { return };
    let Some(sd) = data::sd_get(c, aid) else { return };
    let Some(ch) = ch_of(c, aid) else { return };
    let cc = pc_mapid2jobid(normalize_job(c, aid, &mut s, sd.class_, ch.sex as i32), ch.sex as i32);
    if cc == -1 {
        // Unable to normalize job??
        log::error!("pc_check_skilltree: Unable to normalize job {} for character {} ({}:{})", ch.class, ch.name, ch.account_id, ch.id);
        s.save(c);
        return;
    }
    let spirit = crate::status::sc::has_sce(c, aid, SC_SPIRIT);
    let Some(tree) = skill_tree_db().find(cc as u16).filter(|t| !t.skills.is_empty()) else {
        s.save(c);
        return;
    };
    loop {
        let mut flag = false;
        for e in &tree.skills {
            let skid = e.skill_id;
            let sk_idx = sk::skill_get_index(skid);
            let mut fail = false;
            if s.at(sk_idx).id != 0 {
                continue; // Already learned
            }
            // Checking required skills
            for &(sk_need_id, need_lv) in &e.need {
                let sk_need_idx = sk::skill_get_index(sk_need_id);
                if sk_need_id > 0 {
                    let ns = s.at(sk_need_idx);
                    let sk_need: i16 = if ns.id == 0 || ns.flag == SKILL_FLAG_TEMPORARY || ns.flag == SKILL_FLAG_PLAGIARIZED {
                        0 // Not learned.
                    } else if ns.flag >= SKILL_FLAG_REPLACED_LV_0 {
                        (ns.flag - SKILL_FLAG_REPLACED_LV_0) as i16 // Real lerned level
                    } else {
                        s.checkskill(sk_need_id) as i16
                    };
                    if (sk_need as i32) < need_lv as i32 {
                        fail = true;
                        break;
                    }
                }
            }
            if fail {
                continue;
            }
            if (ch.base_level as u32) < e.baselv || (ch.job_level as u32) < e.joblv {
                continue;
            }
            let Some(skill) = skill_db().find(skid) else { continue };
            let mut sl = s.at(sk_idx);
            if sl.lv == 0 && ((skill.has_inf2(sk::INF2_ISQUEST) && !quest_skill_learn()) || skill.has_inf2(sk::INF2_ISWEDDING) || (skill.has_inf2(sk::INF2_ISSPIRIT) && !spirit)) {
                continue; // Cannot be learned via normal means.
            }
            sl.id = skid;
            s.put(sk_idx, sl);
            flag = true;
        }
        if !flag {
            break;
        }
    }
    s.save(c);
}

// ---------------------------------------------------------------------------------------------- pc_skill

/// pc.cpp pc_skill: grants a skill (ADDSKILL_PERMANENT: set, bound to the skill tree; ADDSKILL_TEMP: an item skill;
/// ADDSKILL_TEMP_ADDLEVEL: an item skill added to the learned level; ADDSKILL_PERMANENT_GRANTED: permanent, outside
/// the skill tree).
pub fn pc_skill(c: &Ctx, aid: u32, skill_id: u16, mut level: i32, type_: i32) -> bool {
    let idx = if skill_id == 0 { 0 } else { sk::skill_get_index(skill_id) };
    if idx == 0 {
        log::error!("pc_skill: Skill with id {skill_id} does not exist in the skill database");
        return false;
    }
    if level > MAX_SKILL_LEVEL {
        log::error!("pc_skill: Skill level {level} too high. Max lv supported is {MAX_SKILL_LEVEL}");
        return false;
    }
    let Some(mut s) = Skills::load(c, aid) else { return false };
    let mut sl = s.at(idx);
    if type_ == ADDSKILL_TEMP_ADDLEVEL && sl.lv as i32 + level > MAX_SKILL_LEVEL {
        log::warn!("pc_skill: Skill level bonus {level} too high. Max lv supported is {MAX_SKILL_LEVEL}. Curr lv is {}. Set to max level.", sl.lv);
        level = MAX_SKILL_LEVEL - sl.lv as i32;
    }
    let passive = |c: &Ctx| skill_get_inf(skill_id) == 0 || summoner_passive(c, aid);
    match type_ {
        ADDSKILL_PERMANENT | ADDSKILL_PERMANENT_GRANTED => {
            // ADDSKILL_PERMANENT: Set skill data overwriting whatever was there before.
            // ADDSKILL_PERMANENT_GRANTED: Permanent granted skills ignore the skill tree
            sl.id = skill_id;
            sl.lv = level as u8;
            sl.flag = if type_ == ADDSKILL_PERMANENT { SKILL_FLAG_PERMANENT } else { SKILL_FLAG_PERM_GRANTED };
            if level == 0 {
                // Remove skill.
                sl.id = 0;
            }
            s.put(idx, sl);
            s.save(c);
            if level == 0 {
                crate::clif_skill::clif_deleteskill(c, aid, skill_id, false);
            } else {
                crate::clif_skill::clif_addskill(c, aid, skill_id);
            }
            if passive(c) {
                // Only recalculate for passive skills.
                crate::status::calc_pc(c, aid, SCO_NONE);
            }
        }
        ADDSKILL_TEMP => {
            // Item bonus skill.
            if sl.id != 0 {
                if sl.flag == SKILL_FLAG_PERMANENT {
                    sl.flag = SKILL_FLAG_REPLACED_LV_0.wrapping_add(sl.lv); // Non-granted skill, store it's level.
                }
            } else {
                sl.id = skill_id;
                sl.flag = SKILL_FLAG_TEMPORARY;
            }
            sl.lv = (sl.lv as i32).max(level) as u8;
            s.put(idx, sl);
            s.save(c);
        }
        ADDSKILL_TEMP_ADDLEVEL => {
            // Add skill bonus on top of what you had.
            if sl.id != 0 {
                if sl.flag == SKILL_FLAG_PERMANENT {
                    sl.flag = SKILL_FLAG_REPLACED_LV_0.wrapping_add(sl.lv); // Store previous level.
                }
            } else {
                sl.id = skill_id;
                sl.flag = SKILL_FLAG_TEMPORARY; // Set that this is a bonus skill.
            }
            sl.lv = (sl.lv as i32 + level) as u8;
            s.put(idx, sl);
            s.save(c);
        }
        _ => return false,
    }
    true
}

/// pc.cpp pc_skill_plagiarism (pc.cpp:5514-5550): sets the player's plagiarised skill (skill_isCopyable 1: the
/// Plagiarism slot, CLONE_SKILL / CLONE_SKILL_LV) or reproduced skill (2: the Reproduce slot, REPRODUCE_SKILL /
/// REPRODUCE_SKILL_LV) at `skill_lv` capped to 1 .. skill_get_max, and shows it (clif_addskill). Unlike skill_do_copy
/// it neither resets the other slot when it holds the same skill nor looks at the copier's own Plagiarism /
/// Reproduce level. False (with rAthena's warning) for an invalid or uncopyable skill.
pub fn pc_skill_plagiarism(c: &Ctx, aid: u32, skill_id: u16, skill_lv: u16) -> bool {
    let skill_id = sk::skill_dummy2skill_id(skill_id);
    let idx = sk::skill_get_index(skill_id);
    // Use skill index, avoiding out-of-bound array [Cydh]
    if idx == 0 {
        log::warn!("pc_skill_plagiarism: invalid skill idx 0 for skill {skill_id}.");
        return false;
    }
    let skill_lv = plagiarism_level(skill_lv, skill_get_max(skill_id));
    let type_ = crate::skill::misc::skill_is_copyable(c, aid, skill_id);
    match type_ {
        1 => {
            pc_skill_plagiarism_reset(c, aid, 1);
            if let Some(mut s) = Skills::load(c, aid) {
                s.meta.cloneskill_idx = idx;
                s.save(c);
            }
            set_char_reg(c, aid, SKILL_VAR_PLAGIARISM, skill_id as i64);
            set_char_reg(c, aid, SKILL_VAR_PLAGIARISM_LV, skill_lv as i64);
        }
        2 => {
            pc_skill_plagiarism_reset(c, aid, 2);
            if let Some(mut s) = Skills::load(c, aid) {
                s.meta.reproduceskill_idx = idx;
                s.save(c);
            }
            set_char_reg(c, aid, SKILL_VAR_REPRODUCE, skill_id as i64);
            set_char_reg(c, aid, SKILL_VAR_REPRODUCE_LV, skill_lv as i64);
        }
        _ => {
            log::warn!("pc_skill_plagiarism: skill {skill_id} is not copyable.");
            return false;
        }
    }
    if let Some(mut s) = Skills::load(c, aid) {
        // sd.status.skill[idx].lv = static_cast<uint8>(skill_lv)
        s.put(idx, Slot { id: skill_id, lv: skill_lv as u8, flag: SKILL_FLAG_PLAGIARIZED });
        s.save(c);
    }
    crate::clif_skill::clif_addskill(c, aid, skill_id);
    true
}

/// pc_skill_plagiarism's `skill_lv = cap_value(skill_lv, 1, skill_get_max(skill_id))` (utils.hpp cap_value: the
/// maximum is tested first), stored back into the uint16.
pub fn plagiarism_level(skill_lv: u16, max: i32) -> u16 {
    let lv = skill_lv as i32;
    (if lv >= max {
        max
    } else if lv <= 1 {
        1
    } else {
        lv
    }) as u16
}

/// pc.cpp pc_skill_plagiarism_reset: clears the plagiarised (type 1) / reproduced (type 2) skill.
pub fn pc_skill_plagiarism_reset(c: &Ctx, aid: u32, type_: u8) -> bool {
    let Some(mut s) = Skills::load(c, aid) else { return false };
    let idx = match type_ {
        1 => s.meta.cloneskill_idx,
        2 => s.meta.reproduceskill_idx,
        _ => {
            log::error!("pc_skill_plagiarism_reset: Unknown type {type_}.");
            return false;
        }
    };
    let sl = s.at(idx);
    if sl.flag == SKILL_FLAG_PLAGIARIZED {
        let skill_id = sl.id;
        s.put(idx, Slot::default());
        if type_ == 1 {
            s.meta.cloneskill_idx = 0;
        } else {
            s.meta.reproduceskill_idx = 0;
        }
        s.save(c);
        crate::clif_skill::clif_deleteskill(c, aid, skill_id, false);
        if type_ == 1 {
            set_char_reg(c, aid, SKILL_VAR_PLAGIARISM, 0);
            set_char_reg(c, aid, SKILL_VAR_PLAGIARISM_LV, 0);
        } else {
            set_char_reg(c, aid, SKILL_VAR_REPRODUCE, 0);
            set_char_reg(c, aid, SKILL_VAR_REPRODUCE_LV, 0);
        }
    }
    true
}

// ---------------------------------------------------------------------------------------------- skill points

/// pc.cpp pc_skillup: spends a skill point on a skill (CZ_UPGRADE_SKILLLEVEL).
pub fn pc_skillup(c: &Ctx, aid: u32, skill_id: u16) {
    let idx = sk::skill_get_index(skill_id);
    if idx == 0 {
        if skill_id != 0 {
            log::error!("pc_skillup: Player attempts to level up invalid skill '{skill_id}'");
        }
        return;
    }
    // Level up guild skill
    if sk::skill_chk_guild(skill_id) {
        crate::guild::guild_skillup(c, aid, skill_id);
        return;
    }
    // Level up homunculus skill: `else if (sd->hd && SKILL_CHK_HOMUN(skill_id)) hom_skillup(sd->hd, skill_id)`
    if crate::homunculus::pc_skillup(c, aid, skill_id) {
        return;
    }
    let Some(mut s) = Skills::load(c, aid) else { return };
    let Some(mut ch) = ch_of(c, aid) else { return };
    let mut sl = s.at(idx);
    if ch.skill_point > 0
        && sl.id != 0
        && sl.flag == SKILL_FLAG_PERMANENT // Don't allow raising while you have granted skills. [Skotlex]
        && (sl.lv as i32) < skill_tree_get_max(skill_id, ch.class as i32)
    {
        sl.lv += 1;
        s.put(idx, sl);
        s.save(c);
        ch.skill_point -= 1;
        put_char(c, &ch);
        let class_ = data::sd_get(c, aid).map(|x| x.class_).unwrap_or(0);
        if skill_get_inf(skill_id) == 0 || summoner_passive(c, aid) {
            crate::status::calc_pc(c, aid, SCO_NONE); // Only recalculate for passive skills.
        } else if ch.skill_point == 0 && pc_is_taekwon_ranker(c, class_, &ch) {
            pc_calc_skilltree(c, aid); // Required to grant all TK Ranker skills.
        } else {
            pc_check_skilltree(c, aid); // Check if a new skill can Lvlup
        }
        let Some(s) = Skills::load(c, aid) else { return };
        let Some(ch) = ch_of(c, aid) else { return };
        let sl = s.at(idx);
        let lv = sl.lv as u16;
        let range = skill_get_range2_pc(c, aid, skill_id, lv, false);
        let upgradable = (lv as i32) < skill_tree_get_max(sl.id, ch.class as i32);
        crate::clif_skill::clif_skillup(c, aid, skill_id, lv, range as u16, upgradable);
        upd(c, aid, SP_SKILLPOINT);
        if skill_id == sk::GN_REMODELING_CART {
            // cart weight info was updated by status_calc_pc
            upd(c, aid, SP_CARTINFO);
        }
        let class_ = data::sd_get(c, aid).map(|x| x.class_).unwrap_or(0);
        if crate::skill::checkskill(c, aid, sk::SG_DEVIL) != 0 && ((class_ & MAPID_THIRDMASK) == MAPID_STAR_EMPEROR || pc_is_maxjoblv(ch.class, ch.job_level as u32)) {
            if let Some(u) = crate::unit::get(c, aid) {
                crate::status::clif::status_change(c, &u, EFST_DEVIL1, 1, 0, 0, 0, 1); // Permanent blind effect from SG_DEVIL.
            }
        }
        if !has_perm(c, aid, crate::rules::PC_PERM_ALL_SKILL) {
            // may skill everything at any time anyways, and this would cause a huge slowdown
            crate::clif_skill::clif_skillinfoblock(c, aid);
        }
    }
}

/// pc.cpp pc_allskillup (@allskill): every skill of the class's tree at its maximum level.
pub fn pc_allskillup(c: &Ctx, aid: u32) -> i32 {
    let Some(mut s) = Skills::load(c, aid) else { return 0 };
    let Some(ch) = ch_of(c, aid) else { return 0 };
    for idx in s.slots.keys().copied().collect::<Vec<u16>>() {
        let mut sl = s.at(idx);
        if sl.flag != SKILL_FLAG_PERMANENT && sl.flag != SKILL_FLAG_PERM_GRANTED && sl.flag != SKILL_FLAG_PLAGIARIZED {
            sl.lv = if sl.flag == SKILL_FLAG_TEMPORARY { 0 } else { sl.flag.wrapping_sub(SKILL_FLAG_REPLACED_LV_0) };
            sl.flag = SKILL_FLAG_PERMANENT;
            if sl.lv == 0 {
                sl.id = 0;
            }
            s.put(idx, sl);
        }
    }
    if !pc_grant_allskills(c, aid, &mut s, true) {
        if let Some(tree) = skill_tree_db().find(ch.class).filter(|t| !t.skills.is_empty()) {
            for e in &tree.skills {
                let sk_id = e.skill_id;
                let sk_idx = sk::skill_get_index(sk_id);
                if sk_idx == 0 {
                    continue;
                }
                let Some(skill) = skill_db().find(sk_id) else { continue };
                if (skill.has_inf2(sk::INF2_ISQUEST) && !quest_skill_learn()) || (skill.has_inf2(sk::INF2_ISWEDDING) || skill.has_inf2(sk::INF2_ISSPIRIT)) || sk_id == sk::SG_DEVIL {
                    continue; // Cannot be learned normally.
                }
                let mut sl = s.at(sk_idx);
                sl.id = sk_id;
                sl.lv = skill_tree_get_max(sk_id, ch.class as i32) as u8; // celest
                s.put(sk_idx, sl);
            }
        }
    }
    s.save(c);
    crate::status::calc_pc(c, aid, SCO_NONE);
    // Required because if you could level up all skills previously, the update will not be sent as only the lv
    // variable changes.
    crate::clif_skill::clif_skillinfoblock(c, aid);
    0
}

/// pc.cpp pc_resetskill. flag&1: resend the skill list and recalculate; flag&2: only count the points spent;
/// flag&4: only the Bard / Dancer skills (changesex). Returns the skill points given back (counted).
pub fn pc_resetskill(c: &Ctx, aid: u32, flag: i32) -> i32 {
    let Some(sd) = data::sd_get(c, aid) else { return 0 };
    if flag & 4 != 0 && (sd.class_ & MAPID_SECONDMASK) != MAPID_BARDDANCER {
        return 0;
    }
    let chk = |id| crate::skill::checkskill(c, aid, id);
    if flag & 2 == 0 {
        // Remove stuff lost when resetting skills.
        let Some(ch) = ch_of(c, aid) else { return 0 };
        // It has been confirmed on official servers that when you reset skills with a ranked Taekwon your skills are
        // not reset (because you have all of them anyway)
        if pc_is_taekwon_ranker(c, sd.class_, &ch) {
            return 0;
        }
        if chk(sk::SG_DEVIL) != 0 && ((sd.class_ & MAPID_THIRDMASK) == MAPID_STAR_EMPEROR || pc_is_maxjoblv(ch.class, ch.job_level as u32)) {
            if let Some(u) = crate::unit::get(c, aid) {
                crate::status::clif::status_change(c, &u, EFST_DEVIL1, 0, 0, 0, 0, 0); // Remove perma blindness due to skill-reset. [Skotlex]
            }
        }
        let Some(u) = crate::unit::get(c, aid) else { return 0 };
        let mut i = u.sc.option;
        if i & OPTION_RIDING != 0 && chk(sk::KN_RIDING) != 0 {
            i &= !OPTION_RIDING;
        }
        if i & OPTION_FALCON != 0 && chk(sk::HT_FALCON) != 0 {
            i &= !OPTION_FALCON;
        }
        if i & OPTION_DRAGON != 0 && chk(sk::RK_DRAGONTRAINING) != 0 {
            i &= !OPTION_DRAGON;
        }
        if i & OPTION_WUG != 0 && chk(sk::RA_WUGMASTERY) != 0 {
            i &= !OPTION_WUG;
        }
        if i & OPTION_WUGRIDER != 0 && chk(sk::RA_WUGRIDER) != 0 {
            i &= !OPTION_WUGRIDER;
        }
        if i & OPTION_MADOGEAR != 0 && (sd.class_ & MAPID_THIRDMASK) == MAPID_MECHANIC {
            i &= !OPTION_MADOGEAR;
        }
        // NEW_CARTS
        if crate::status::sc::has_sce(c, aid, SC_PUSH_CART) {
            crate::cart::pc_setcart(c, aid, 0);
        }
        if crate::unit::get(c, aid).map(|u| u.sc.option != i).unwrap_or(false) {
            pc_setoption(c, aid, i, 0);
        }
        // if( hom_is_active(sd->hd) && pc_checkskill(sd, AM_CALLHOMUN) ) hom_vaporize(sd, HOM_ST_ACTIVE) (pc.cpp:9468)
        if chk(sk::AM_CALLHOMUN) != 0 {
            crate::homunculus::vaporize_if_active(c, aid);
        }
        if crate::status::sc::has_sce(c, aid, SC_SPRITEMABLE) && chk(sk::SU_SPRITEMABLE) != 0 {
            crate::status::sc::status_change_end(c, aid, SC_SPRITEMABLE, 0);
        }
        if crate::status::sc::has_sce(c, aid, SC_SOULATTACK) && chk(sk::SU_SOULATTACK) != 0 {
            crate::status::sc::status_change_end(c, aid, SC_SOULATTACK, 0);
        }
    }
    let Some(mut s) = Skills::load(c, aid) else { return 0 };
    let class_ = data::sd_get(c, aid).map(|x| x.class_).unwrap_or(sd.class_);
    let mut skill_point: i32 = 0;
    for idx in s.slots.keys().copied().collect::<Vec<u16>>() {
        let skill_id = idx_skill(idx);
        let mut sl = s.at(idx);
        let lv = sl.lv;
        if lv == 0 || skill_id == 0 {
            continue;
        }
        let Some(skill) = skill_db().find(skill_id) else { continue };
        if skill.has_inf2(sk::INF2_ISWEDDING) || skill.has_inf2(sk::INF2_ISSPIRIT) {
            continue; // Avoid reseting wedding/linker skills.
        }
        // Don't reset trick dead if not a novice/baby
        if skill_id == sk::NV_TRICKDEAD && (class_ & MAPID_SECONDMASK) != MAPID_NOVICE {
            sl.lv = 0;
            sl.flag = SKILL_FLAG_PERMANENT;
            s.put(idx, sl);
            continue;
        }
        // do not reset basic skill
        if skill_id == sk::NV_BASIC && (class_ & MAPID_SECONDMASK) != MAPID_NOVICE {
            continue;
        }
        if sl.flag == SKILL_FLAG_PERM_GRANTED {
            continue;
        }
        if flag & 4 != 0 && !skill_ischangesex(skill_id) {
            continue;
        }
        if skill.has_inf2(sk::INF2_ISQUEST) && !quest_skill_learn() {
            // Only handle quest skills in a special way when you can't learn them manually
            if quest_skill_reset() && flag & 2 == 0 {
                // Wipe them
                sl.lv = 0;
                sl.flag = SKILL_FLAG_PERMANENT;
                s.put(idx, sl);
            }
            continue;
        }
        if sl.flag == SKILL_FLAG_PERMANENT {
            skill_point += lv as i32;
        } else if sl.flag >= SKILL_FLAG_REPLACED_LV_0 {
            skill_point += (sl.flag - SKILL_FLAG_REPLACED_LV_0) as i32;
        }
        if flag & 2 == 0 {
            // reset
            sl.lv = 0;
            sl.flag = SKILL_FLAG_PERMANENT;
            s.put(idx, sl);
        }
    }
    s.save(c);
    if flag & 2 != 0 || skill_point == 0 {
        return skill_point;
    }
    if let Some(mut ch) = ch_of(c, aid) {
        // sd->status.skill_point += skill_point (uint32, pc.cpp:9529)
        ch.skill_point = ch.skill_point.wrapping_add(skill_point as u32);
        put_char(c, &ch);
    }
    if flag & 1 != 0 {
        upd(c, aid, SP_SKILLPOINT);
        crate::clif_skill::clif_skillinfoblock(c, aid);
        crate::status::calc_pc(c, aid, SCO_FORCE);
    }
    skill_point
}

/// The equipment the character may no longer wear comes off (the `pc_isequip` loop of pc_resetlvl / pc_jobchange /
/// map_quit: pc_unequipitem flag 2).
fn unequip_invalid(c: &Ctx, aid: u32) {
    for i in 0..crate::inventory::EQI_MAX {
        let idx = crate::status::calc::equip_index(c, aid).get(i).copied().unwrap_or(-1);
        if idx >= 0 && crate::pc_equip::pc_isequip(c, aid, idx as usize) != crate::inventory::ITEM_EQUIP_ACK_OK {
            crate::pc_equip::pc_unequipitem(c, aid, idx as i32, 2);
        }
    }
}

/// pc.cpp pc_resetlvl (script resetlvl). type 1: as after a rebirth; 2: base / job level 1, no skill points; 3: base
/// level 1 (skills kept); 4: job level 1.
pub fn pc_resetlvl(c: &Ctx, aid: u32, type_: i32) -> i32 {
    if ch_of(c, aid).is_none() {
        return 0;
    }
    if type_ != 3 {
        // Also reset skills
        pc_resetskill(c, aid, 0);
    }
    let Some(mut ch) = ch_of(c, aid) else { return 0 };
    if type_ == 1 {
        ch.skill_point = 0;
        ch.trait_point = 0;
        ch.base_level = 1;
        ch.job_level = 1;
        ch.base_exp = 0;
        ch.job_exp = 0;
        if let Some(mut u) = crate::unit::get(c, aid) {
            if u.sc.option != 0 {
                u.sc.option = 0; // (set directly: no packet, as rAthena does)
                crate::unit::save(c, &u);
            }
        }
        ch.str = 1;
        ch.agi = 1;
        ch.vit = 1;
        ch.int = 1;
        ch.dex = 1;
        ch.luk = 1;
        ch.pow = 0;
        ch.sta = 0;
        ch.wis = 0;
        ch.spl = 0;
        ch.con = 0;
        ch.crt = 0;
        let novice_high = ch.class as i32 == JOB_NOVICE_HIGH;
        if novice_high {
            ch.status_point = 100; // not 88 [celest]
        }
        put_char(c, &ch);
        if novice_high {
            // give platinum skills upon changing
            pc_skill(c, aid, sk::NV_FIRSTAID, 1, ADDSKILL_PERMANENT);
            pc_skill(c, aid, sk::NV_TRICKDEAD, 1, ADDSKILL_PERMANENT);
        }
        ch = match ch_of(c, aid) {
            Some(x) => x,
            None => return 0,
        };
    }
    if type_ == 2 {
        ch.skill_point = 0;
        ch.base_level = 1;
        ch.job_level = 1;
        ch.base_exp = 0;
        ch.job_exp = 0;
    }
    if type_ == 3 {
        ch.base_level = 1;
        ch.base_exp = 0;
    }
    if type_ == 4 {
        ch.job_level = 1;
        ch.job_exp = 0;
    }
    put_char(c, &ch);
    for t in [
        SP_STATUSPOINT,
        SP_TRAITPOINT,
        SP_STR,
        SP_AGI,
        SP_VIT,
        SP_INT,
        SP_DEX,
        SP_LUK,
        SP_POW,
        SP_STA,
        SP_WIS,
        SP_SPL,
        SP_CON,
        SP_CRT,
        SP_BASELEVEL,
        SP_JOBLEVEL,
        SP_STATUSPOINT,
        SP_BASEEXP,
        SP_JOBEXP,
        SP_NEXTBASEEXP,
        SP_NEXTJOBEXP,
        SP_SKILLPOINT,
        SP_USTR, // Updates needed stat points - Valaris
        SP_UAGI,
        SP_UVIT,
        SP_UINT,
        SP_UDEX,
        SP_ULUK, // End Addition
        SP_UPOW,
        SP_USTA,
        SP_UWIS,
        SP_USPL,
        SP_UCON,
        SP_UCRT,
    ] {
        upd(c, aid, t);
    }
    // unequip items that can't be equipped by base 1 [Valaris]
    unequip_invalid(c, aid);
    if (type_ == 1 || type_ == 2 || type_ == 3) && ch_of(c, aid).map(|x| x.party_id != 0).unwrap_or(false) {
        crate::party::party_send_levelup(c, aid);
    }
    crate::status::calc_pc(c, aid, SCO_FORCE);
    crate::clif_skill::clif_skillinfoblock(c, aid);
    0
}

// ---------------------------------------------------------------------------------------------- job change

/// pc.cpp pc_resetfeel: the Star Gladiator feel maps are forgotten (sd->feel_map: pc_taekwon.rs, and the registers).
pub fn pc_resetfeel(c: &Ctx, aid: u32) -> i32 {
    for (i, v) in SG_FEEL_VARS.iter().enumerate() {
        crate::pc_taekwon::resetfeel(c, aid, i);
        set_char_reg(c, aid, v, 0);
    }
    0
}

/// pc.cpp pc_resethate: the Star Gladiator hated monsters are forgotten (sd->hate_mob: pc_taekwon.rs, and the
/// registers).
pub fn pc_resethate(c: &Ctx, aid: u32) -> i32 {
    for (i, v) in SG_HATE_VARS.iter().enumerate() {
        crate::pc_taekwon::resethate(c, aid, i);
        set_char_reg(c, aid, v, 0);
    }
    0
}

/// status.cpp StatusDatabase::changeSkillTree(sd): the status changes of the skills of the character's (current)
/// tree end.
pub(crate) fn change_skill_tree(c: &Ctx, aid: u32, class_: u16) {
    let Some(tree) = skill_tree_db().find(class_) else { return };
    for e in &tree.skills {
        let sc = sk::skill_get_sc(e.skill_id) as i32;
        if sc > SC_COMMON_MAX && sc < SC_MAX && crate::status::sc::has_sce(c, aid, sc) {
            crate::status::sc::status_change_end(c, aid, sc, 0);
        }
    }
}

/// status.cpp status_set_viewdata for a player: the look of `class_` (a player class: the character's own look with
/// the mounted sprite; a monster / NPC class: that unit's view data).
pub fn status_set_viewdata_pc(c: &Ctx, aid: u32, class_: i32) {
    let Some(ch) = ch_of(c, aid) else { return };
    if crate::clif::pcdb_checkid(class_) {
        let mut cls = class_;
        let option = crate::unit::get(c, aid).map(|u| u.sc.option).unwrap_or(0);
        if option & OPTION_RIDING != 0 {
            // Adapt class to a Mounted one.
            cls = match class_ {
                JOB_KNIGHT => JOB_KNIGHT2,
                JOB_CRUSADER => JOB_CRUSADER2,
                JOB_LORD_KNIGHT => JOB_LORD_KNIGHT2,
                JOB_PALADIN => JOB_PALADIN2,
                JOB_BABY_KNIGHT => JOB_BABY_KNIGHT2,
                JOB_BABY_CRUSADER => JOB_BABY_CRUSADER2,
                x => x,
            };
        }
        // sd->update_look(LOOK_WEAPON / LOOK_SHIELD) and the headgear / robe looks (pc_equip.rs)
        crate::pc_equip::status_set_viewdata_items(c, aid);
        let Some(mut u) = crate::unit::get(c, aid) else { return };
        u.vd.class = cls;
        u.vd.head_top = ch.head_top as i32;
        u.vd.head_mid = ch.head_mid as i32;
        u.vd.head_bottom = ch.head_bottom as i32;
        u.vd.hair = (ch.hair as i32).clamp(crate::conf::min_hair_style(), crate::conf::max_hair_style());
        u.vd.hair_color = (ch.hair_color as i32).clamp(crate::conf::min_hair_color(), crate::conf::max_hair_color());
        u.vd.clothes_color = (ch.clothes_color as i32).clamp(crate::conf::min_cloth_color(), crate::conf::max_cloth_color());
        u.vd.body2 = ch.body as i32;
        u.vd.sex = ch.sex;
        u.vd.robe = ch.robe as i32;
        if u.vd.clothes_color != 0 && IGNORE_PALETTE && option & OPTION_COSTUME != 0 {
            u.vd.clothes_color = 0; // (the *_ignorepalette settings are all off)
        }
        if option & OPTION_COSTUME != 0 {
            u.vd.body2 = cls;
        }
        crate::unit::save(c, &u);
    } else if crate::db::mobs::mobdb_checkid(class_) != 0 || crate::db::mobs::mob_is_clone(class_) != 0 {
        let Some(vd) = crate::db::mobs::mob_get_viewdata(class_) else { return };
        let Some(mut u) = crate::unit::get(c, aid) else { return };
        let l = |i: usize| vd.look.get(i).copied().unwrap_or(0);
        use crate::db::mobs as m;
        u.vd = crate::unit::ViewData {
            class: l(m::LOOK_BASE),
            hair: l(m::LOOK_HAIR),
            weapon: l(m::LOOK_WEAPON),
            head_bottom: l(m::LOOK_HEAD_BOTTOM),
            head_top: l(m::LOOK_HEAD_TOP),
            head_mid: l(m::LOOK_HEAD_MID),
            hair_color: l(m::LOOK_HAIR_COLOR),
            clothes_color: l(m::LOOK_CLOTHES_COLOR),
            shield: l(m::LOOK_SHIELD),
            shoes: l(m::LOOK_SHOES),
            body: l(m::LOOK_BODY),
            robe: l(m::LOOK_ROBE),
            body2: l(m::LOOK_BODY2),
            sex: vd.sex as u8,
            dead_sit: vd.dead_sit,
        };
        crate::unit::save(c, &u);
    } else {
        // npc_get_viewdata / hom_get_viewdata (homunculus.rs) / mercenary_get_viewdata / elemental_get_viewdata
        // (status.cpp:9385-9392): NPC classes take the NPC view (look[LOOK_BASE] = class)
        let npc = (class_ > 44 && class_ < 126) || class_ == crate::npc::JT_HIDDEN_WARP_NPC || (class_ > 400 && class_ < 1000) || class_ == crate::npc::JT_INVISIBLE || (class_ > 10000 && class_ < 20000);
        let comp_vd = if crate::db::misc::homunculus::homdb_checkid(class_) {
            // hom_get_viewdata: look[LOOK_BASE] = the class only (homunculus.cpp hom_viewdb)
            crate::homunculus::hom_get_viewdata(class_)
        } else if crate::db::misc::mercenary::mercenary_db().exists(class_) {
            crate::db::misc::mercenary::mercenary_db().find(class_).map(|m| crate::mob::status::view_from_db(&m.vd))
        } else if crate::db::misc::elemental::elemental_db().exists(class_) {
            crate::db::misc::elemental::elemental_get_viewdata(class_).map(crate::mob::status::view_from_db)
        } else {
            None
        };
        if let (false, Some(vd)) = (npc, comp_vd) {
            // memcpy(&sd->vd, vd, sizeof(struct view_data))
            let Some(mut u) = crate::unit::get(c, aid) else { return };
            u.vd = vd;
            crate::unit::save(c, &u);
        } else if npc {
            let Some(mut u) = crate::unit::get(c, aid) else { return };
            u.vd = crate::unit::ViewData { class: class_, ..Default::default() };
            crate::unit::save(c, &u);
        } else {
            log::error!("status_set_viewdata (PC): No view data for class {class_}");
        }
    }
}

/// pc.cpp pc_setoption: the OPTION_* bits change (mounts, falcon, wolf, mado gear, flying look).
pub fn pc_setoption(c: &Ctx, aid: u32, type_: i32, subtype: i32) {
    use crate::pc_equip::{clif_changelook, LOOK_BASE, LOOK_BODY2, LOOK_CLOTHES_COLOR};
    let Some(mut u) = crate::unit::get(c, aid) else { return };
    let p_type = u.sc.option;
    // Option has to be changed client-side before the class sprite or it won't always work (eg: Wedding sprite) [Skotlex]
    u.sc.option = type_;
    crate::unit::save(c, &u);
    crate::clif::changeoption(c, &u, u.sc.option, None);
    let dragon = crate::skill::checkskill(c, aid, sk::RK_DRAGONTRAINING) > 0;
    let load = |efst: i32, on: bool| {
        if let Some(u) = crate::unit::get(c, aid) {
            crate::status::clif::status_change(c, &u, efst, on as i32, 0, 0, 0, 0);
        }
    };
    if (type_ & OPTION_RIDING != 0 && p_type & OPTION_RIDING == 0) || (type_ & OPTION_DRAGON != 0 && p_type & OPTION_DRAGON == 0 && dragon) {
        // Mounting
        load(EFST_RIDING, true);
        crate::status::calc_pc(c, aid, SCO_NONE);
    } else if (type_ & OPTION_RIDING == 0 && p_type & OPTION_RIDING != 0) || (type_ & OPTION_DRAGON == 0 && p_type & OPTION_DRAGON != 0 && dragon) {
        // Dismount
        load(EFST_RIDING, false);
        crate::status::calc_pc(c, aid, SCO_NONE);
    }
    // (NEW_CARTS: the cart is a status change, not an option)
    if type_ & OPTION_FALCON != 0 && p_type & OPTION_FALCON == 0 {
        load(EFST_FALCON, true); // Falcon ON
    } else if type_ & OPTION_FALCON == 0 && p_type & OPTION_FALCON != 0 {
        load(EFST_FALCON, false); // Falcon OFF
    }
    if type_ & OPTION_WUGRIDER != 0 && p_type & OPTION_WUGRIDER == 0 {
        // Mounting
        load(EFST_WUGRIDER, true);
        crate::status::calc_pc(c, aid, SCO_NONE);
    } else if type_ & OPTION_WUGRIDER == 0 && p_type & OPTION_WUGRIDER != 0 {
        // Dismount
        load(EFST_WUGRIDER, false);
        crate::status::calc_pc(c, aid, SCO_NONE);
    }
    if type_ & OPTION_MADOGEAR != 0 && p_type & OPTION_MADOGEAR == 0 {
        if let Some(u) = crate::unit::get(c, aid) {
            crate::status::sc::sc_start(c, Some(&u), &u, SC_MADOGEAR, 100, subtype, crate::status::sc::INFINITE_TICK);
        }
    } else if type_ & OPTION_MADOGEAR == 0 && p_type & OPTION_MADOGEAR != 0 {
        crate::status::sc::status_change_end(c, aid, SC_MADOGEAR, 0);
    }
    let mut new_look = 0;
    if type_ & OPTION_FLYING != 0 && p_type & OPTION_FLYING == 0 {
        new_look = JOB_STAR_GLADIATOR2;
    } else if type_ & OPTION_FLYING == 0 && p_type & OPTION_FLYING != 0 {
        new_look = -1;
    }
    if crate::atcmd_disguise::disguise_of(c, aid) != 0 || new_look == 0 {
        return; // Disguises break sprite changes
    }
    if new_look < 0 {
        // Restore normal look.
        let class_ = ch_of(c, aid).map(|x| x.class as i32).unwrap_or(0);
        status_set_viewdata_pc(c, aid, class_);
        new_look = crate::unit::get(c, aid).map(|u| u.vd.class).unwrap_or(0);
    }
    // Stop attacking on new view change (to prevent wedding/santa attacks).
    if let Some(mut u) = crate::unit::get(c, aid) {
        crate::unit::stop_attack(c, &mut u);
    }
    clif_changelook(c, aid, LOOK_BASE, new_look);
    let Some(u) = crate::unit::get(c, aid) else { return };
    if u.vd.clothes_color != 0 {
        clif_changelook(c, aid, LOOK_CLOTHES_COLOR, u.vd.clothes_color);
    }
    if u.vd.body2 != 0 {
        clif_changelook(c, aid, LOOK_BODY2, u.vd.body2);
    }
    crate::clif_skill::clif_skillinfoblock(c, aid); // Skill list needs to be updated after base change.
}

/// pc.hpp pc_check_weapontype.
fn pc_check_weapontype(c: &Ctx, aid: u32, type_: i32) -> bool {
    let weapon = ch_of(c, aid).map(|x| x.weapon as i32).unwrap_or(0);
    let bits = if weapon < MAX_WEAPON_TYPE {
        1i64 << weapon
    } else {
        let (w1, w2) = crate::inventory::st(c, aid).map(|s| (s.weapontype1 as i32, s.weapontype2 as i32)).unwrap_or((0, 0));
        (1i64 << w1) | (1i64 << w2) | (1i64 << (weapon & 63))
    };
    (type_ as i64) & bits != 0
}

/// pc.cpp pc_checkallowskill: status changes whose weapon / shield requirement no longer holds end.
pub fn pc_checkallowskill(c: &Ctx, aid: u32) {
    let scl = crate::status::sc::load(c, aid);
    if scl.empty() {
        return;
    }
    let (weapon, shield) = ch_of(c, aid).map(|x| (x.weapon, x.shield)).unwrap_or((0, 0));
    let class_ = data::sd_get(c, aid).map(|x| x.class_).unwrap_or(0);
    for it in crate::db::misc::status::status_db().iter() {
        let status = it.type_;
        if it.has_flag(SCF_REQUIREWEAPON) {
            // Skills requiring specific weapon types
            if status == SC_DANCING && !dancing_weaponswitch_fix() {
                continue;
            }
            if crate::status::sc::has_sce(c, aid, status) && !pc_check_weapontype(c, aid, sk::skill_get_weapontype(it.skill_id)) {
                crate::status::sc::status_change_end(c, aid, status, 0);
            }
        }
        if it.has_flag(SCF_REQUIRENOWEAPON) && crate::status::sc::has_sce(c, aid, status) && weapon != 0 {
            crate::status::sc::status_change_end(c, aid, status, 0);
        }
        if it.has_flag(SCF_REQUIRESHIELD) {
            // Skills requiring a shield
            // Only crusader-type classes require a shield for Defender
            if status == SC_DEFENDER && (class_ & MAPID_SECONDMASK) != MAPID_CRUSADER {
                continue;
            }
            if crate::status::sc::has_sce(c, aid, status) && shield == 0 {
                crate::status::sc::status_change_end(c, aid, status, 0);
            }
        }
    }
}

/// pc.cpp pc_jobchange: `job` a JOB_* id, `upper` 1 JOBL_UPPER / 2 JOBL_BABY (anything else: as given).
pub fn pc_jobchange(c: &Ctx, aid: u32, job: i32, upper: i32) -> bool {
    use crate::pc_equip::{clif_changelook, LOOK_BASE, LOOK_BODY2, LOOK_CLOTHES_COLOR, LOOK_HAIR};
    if job < 0 {
        return false;
    }
    let Some(sd) = data::sd_get(c, aid) else { return false };
    let Some(ch0) = ch_of(c, aid) else { return false };
    // Normalize job.
    let mut b_class = pc_jobid2mapid(job as u16);
    if b_class == u64::MAX {
        return false;
    }
    match upper as i8 {
        1 => b_class |= JOBL_UPPER,
        2 => b_class |= JOBL_BABY,
        _ => {}
    }
    // This will automatically adjust bard/dancer classes to the correct gender. That is, if you try to jobchange into
    // dancer, it will turn you to bard.
    let job = pc_mapid2jobid(b_class, ch0.sex as i32);
    if job == -1 {
        return false;
    }
    if b_class == sd.class_ {
        return false; // Nothing to change.
    }
    // If the job does not exist in the job db, dont allow changing to it
    if job_db().find(job as u16).is_none() {
        return false;
    }
    if let Some(mut s) = Skills::load(c, aid) {
        if b_class & JOBL_FOURTH != 0 && sd.class_ & JOBL_FOURTH == 0 {
            // Changing to 4th job
            s.meta.change_level_4th = ch0.job_level as u8;
            s.save(c);
            set_char_reg(c, aid, JOBCHANGE4TH_VAR, s.meta.change_level_4th as i64);
        } else if b_class & JOBL_THIRD != 0 && sd.class_ & JOBL_THIRD == 0 {
            // changing from 2nd to 3rd job
            s.meta.change_level_3rd = ch0.job_level as u8;
            s.save(c);
            set_char_reg(c, aid, JOBCHANGE3RD_VAR, s.meta.change_level_3rd as i64);
        } else if b_class & JOBL_2 != 0 && sd.class_ & JOBL_2 == 0 {
            // changing from 1st to 2nd job
            s.meta.change_level_2nd = ch0.job_level as u8;
            s.save(c);
            set_char_reg(c, aid, JOBCHANGE2ND_VAR, s.meta.change_level_2nd as i64);
        }
        if s.meta.cloneskill_idx > 0 {
            pc_skill_plagiarism_reset(c, aid, 1);
        }
        if Skills::load(c, aid).map(|s| s.meta.reproduceskill_idx > 0).unwrap_or(false) {
            pc_skill_plagiarism_reset(c, aid, 2);
        }
    }
    if (b_class & MAPID_SECONDMASK) != (sd.class_ & MAPID_SECONDMASK) {
        // Things to remove when changing class tree.
        change_skill_tree(c, aid, ch0.class);
    }
    if (sd.class_ & MAPID_SECONDMASK) == MAPID_STAR_GLADIATOR && (b_class & MAPID_SECONDMASK) != MAPID_STAR_GLADIATOR {
        // going off star glad lineage, reset feel and hate to not store no-longer-used vars in the database
        pc_resetfeel(c, aid);
        pc_resethate(c, aid);
    }
    let Some(mut ch) = ch_of(c, aid) else { return false };
    ch.class = job as u16;
    // Reset body style before changing job to avoid errors since not every job has a alternate outfit.
    ch.body = ch.class;
    let previous_class = data::sd_get(c, aid).map(|x| x.class_).unwrap_or(sd.class_);
    let fame_flag = crate::status::calc::pc_famerank(c, ch.id, previous_class & MAPID_SECONDMASK);
    ch.job_level = 1;
    ch.job_exp = 0;
    put_char(c, &ch);
    data::with_sd(c, aid, |x| x.class_ = b_class);
    if ch.base_level as u32 > pc_maxbaselv(ch.class) {
        ch.base_level = pc_maxbaselv(ch.class) as u16;
        ch.base_exp = 0;
        put_char(c, &ch);
        crate::status::progress::pc_resetstate(c, aid);
        for t in [SP_STATUSPOINT, SP_TRAITPOINT, SP_BASELEVEL, SP_BASEEXP, SP_NEXTBASEEXP] {
            upd(c, aid, t);
        }
    }
    let t_new = b_class & JOBL_UPPER != 0 || pc_is_primary_fourth(b_class);
    let t_old = previous_class & JOBL_UPPER != 0 || pc_is_primary_fourth(previous_class);
    // Give or reduce transcendent status points
    if t_new && !t_old {
        // Change from a non t class to a t class -> give points
        if let Some(mut ch) = ch_of(c, aid) {
            ch.status_point = ch.status_point.wrapping_add(crate::conf::transcendent_status_points()); // uint32 +=
            put_char(c, &ch);
        }
        upd(c, aid, SP_STATUSPOINT);
    } else if !t_new && t_old {
        // Change from a t class to a non t class -> remove points
        let Some(mut ch) = ch_of(c, aid) else { return false };
        if ch.status_point < crate::conf::transcendent_status_points() {
            // The player already used his bonus points, so we have to reset his status points
            crate::status::progress::pc_resetstate(c, aid);
        } else {
            ch.status_point -= crate::conf::transcendent_status_points();
            put_char(c, &ch);
            upd(c, aid, SP_STATUSPOINT);
        }
    }
    // Update increase cost indicator
    for t in [SP_USTR, SP_UAGI, SP_UVIT, SP_UINT, SP_UDEX, SP_ULUK] {
        upd(c, aid, t);
    }
    // Give or reduce trait status points
    if pc_is_trait_job(b_class) && !pc_is_trait_job(previous_class) {
        // Change to a trait job.
        if let Some(mut ch) = ch_of(c, aid) {
            ch.trait_point += crate::conf::trait_points_job_change();
            put_char(c, &ch);
        }
        for t in [SP_TRAITPOINT, SP_UPOW, SP_USTA, SP_UWIS, SP_USPL, SP_UCON, SP_UCRT] {
            upd(c, aid, t);
        }
    } else if !pc_is_trait_job(b_class) && pc_is_trait_job(previous_class) {
        // Change to a non trait job.
        let Some(mut ch) = ch_of(c, aid) else { return false };
        if ch.trait_point < crate::conf::trait_points_job_change() {
            // Player may have already used the trait status points. Force a reset.
            crate::status::progress::pc_resetstate(c, aid);
        } else {
            ch.trait_point = 0;
            put_char(c, &ch);
            for t in [SP_TRAITPOINT, SP_UPOW, SP_USTA, SP_UWIS, SP_USPL, SP_UCON, SP_UCRT] {
                upd(c, aid, t);
            }
        }
    }
    for t in [SP_JOBLEVEL, SP_JOBEXP, SP_NEXTJOBEXP] {
        upd(c, aid, t);
    }
    // unequip invalid item for class
    unequip_invalid(c, aid);
    // Change look, if disguised, you need to undisguise to correctly calculate new job sprite without
    if crate::atcmd_disguise::disguise_of(c, aid) != 0 {
        crate::atcmd_disguise::pc_disguise(c, aid, 0);
    }
    status_set_viewdata_pc(c, aid, job);
    let Some(u) = crate::unit::get(c, aid) else { return false };
    clif_changelook(c, aid, LOOK_BASE, u.vd.class); // move sprite update to prevent client crashes with incompatible equipment [Valaris]
    // PACKETVER >= 20151001: Update player's head (only matters when switching to or from Doram)
    let hair = crate::unit::get(c, aid).map(|u| u.vd.hair).unwrap_or(0);
    clif_changelook(c, aid, LOOK_HAIR, hair);
    let cc = crate::unit::get(c, aid).map(|u| u.vd.clothes_color).unwrap_or(0);
    clif_changelook(c, aid, LOOK_CLOTHES_COLOR, cc);
    let b2 = crate::unit::get(c, aid).map(|u| u.vd.body2).unwrap_or(0);
    clif_changelook(c, aid, LOOK_BODY2, b2);
    // Update skill tree.
    pc_calc_skilltree(c, aid);
    crate::clif_skill::clif_skillinfoblock(c, aid);
    // if (sd->ed) elemental_delete(sd->ed) (elemental.rs)
    crate::elemental::master_drops(c, aid);
    if crate::pc::get(c, aid).map(|p| p.vending).unwrap_or(false) {
        crate::vending::vending_closevending(c, aid);
    }
    if crate::pc::get(c, aid).map(|p| p.buyingstore).unwrap_or(false) {
        crate::buyingstore::buyingstore_close(c, aid);
    }
    // jobchange_killclone: the character's monster clones die (monster clones, mob agent / phase 4)
    // Remove peco/cart/falcon
    let chk = |id| crate::skill::checkskill(c, aid, id);
    let option = crate::unit::get(c, aid).map(|u| u.sc.option).unwrap_or(0);
    let mut i = option;
    if i & OPTION_RIDING != 0 && chk(sk::KN_RIDING) == 0 {
        i &= !OPTION_RIDING;
    }
    if i & OPTION_FALCON != 0 && chk(sk::HT_FALCON) == 0 {
        i &= !OPTION_FALCON;
    }
    if i & OPTION_DRAGON != 0 && chk(sk::RK_DRAGONTRAINING) == 0 {
        i &= !OPTION_DRAGON;
    }
    if i & OPTION_WUGRIDER != 0 && chk(sk::RA_WUGMASTERY) == 0 {
        i &= !OPTION_WUGRIDER;
    }
    if i & OPTION_WUG != 0 && chk(sk::RA_WUGMASTERY) == 0 {
        i &= !OPTION_WUG;
    }
    if i & OPTION_MADOGEAR != 0 {
        i &= !OPTION_MADOGEAR; // You do not need a skill for this.
    }
    // NEW_CARTS
    if crate::status::sc::has_sce(c, aid, SC_PUSH_CART) && chk(sk::MC_PUSHCART) == 0 {
        crate::cart::pc_setcart(c, aid, 0);
    }
    if crate::unit::get(c, aid).map(|u| u.sc.option != i).unwrap_or(false) {
        pc_setoption(c, aid, i, 0);
    }
    // if(hom_is_active(sd->hd) && !pc_checkskill(sd, AM_CALLHOMUN)) hom_vaporize(sd, HOM_ST_ACTIVE) (pc.cpp:11043)
    if chk(sk::AM_CALLHOMUN) == 0 {
        crate::homunculus::vaporize_if_active(c, aid);
    }
    if crate::status::sc::has_sce(c, aid, SC_SPRITEMABLE) && chk(sk::SU_SPRITEMABLE) == 0 {
        crate::status::sc::status_change_end(c, aid, SC_SPRITEMABLE, 0);
    }
    if crate::status::sc::has_sce(c, aid, SC_SOULATTACK) && chk(sk::SU_SOULATTACK) == 0 {
        crate::status::sc::status_change_end(c, aid, SC_SOULATTACK, 0);
    }
    if crate::status::sc::has_sce(c, aid, SC_SPIRIT) {
        crate::status::sc::status_change_end(c, aid, SC_SPIRIT, 0);
    }
    if ch_of(c, aid).map(|x| x.manner < 0).unwrap_or(false) {
        crate::status::clif::changemanner(c, aid);
        upd(c, aid, SP_MANNER);
    }
    crate::status::calc_pc(c, aid, SCO_FORCE);
    pc_checkallowskill(c, aid);
    crate::pc_equip::pc_equiplookall(c, aid);
    crate::quest::pc_show_questinfo(c, aid);
    let base_level = ch_of(c, aid).map(|x| x.base_level as i32).unwrap_or(0);
    crate::achievement::achievement_update_objective(c, Some(aid), crate::db::misc::achievement::AG_JOB_CHANGE, &[base_level, job]);
    let Some(ch) = ch_of(c, aid) else { return true };
    if ch.party_id != 0 {
        if let Some(mut p) = crate::party::party_search(c, ch.party_id) {
            if let Some(m) = p.party.member.iter_mut().find(|m| m.char_id == ch.id) {
                m.class = ch.class;
                c.db().party_data().party_id().update(p);
                if let Some(u) = crate::unit::get(c, aid) {
                    crate::party::clif_party_job_and_level(c, &u);
                }
            }
        }
    }
    // pc.cpp:11076: every job change saves the character (the quest and achievement logs go to their tables now,
    // which decides their row order)
    crate::chrif::chrif_save(c, aid, crate::chrif::CSAVE_NORMAL);
    //if you were previously famous, not anymore. (pc.cpp:11078-11089: the Blacksmith / Alchemist / Taekwon rankings)
    if fame_flag != 0 {
        crate::fame::chrif_buildfamelist(c);
    } else if crate::pc::character_of(c, aid).map(|ch| ch.fame > 0).unwrap_or(false) {
        //It may be that now they are famous?
        let class_ = crate::status::data::sd_get(c, aid).map(|s| s.class_).unwrap_or(0);
        if matches!(class_ & MAPID_SECONDMASK, MAPID_BLACKSMITH | MAPID_ALCHEMIST | MAPID_TAEKWON) {
            crate::fame::chrif_buildfamelist(c);
        }
    }
    true
}

// ---------------------------------------------------------------------------------------------- login and logout

/// The char server's save of the skill slots (char.cpp mmo_char_tosql): which slots are stored and as what.
/// Returns the slot as it comes back from the `skill` table, or None when it is not stored.
fn saved_form(s: Slot, skill_id: u16) -> Option<Slot> {
    if s.id == 0 || s.flag == SKILL_FLAG_TEMPORARY {
        return None;
    }
    if s.lv == 0 && (s.flag == SKILL_FLAG_PERM_GRANTED || s.flag == SKILL_FLAG_PERMANENT) {
        return None;
    }
    if s.flag != SKILL_FLAG_PERMANENT && s.flag != SKILL_FLAG_PERM_GRANTED && s.flag.wrapping_sub(SKILL_FLAG_REPLACED_LV_0) == 0 {
        return None;
    }
    if s.flag != SKILL_FLAG_PERMANENT && s.flag != SKILL_FLAG_PERM_GRANTED && s.flag < SKILL_FLAG_REPLACED_LV_0 {
        // PLAGIARIZED / TMP_COMBO (never left after pc_clean_skilltree): `flag - SKILL_FLAG_REPLACED_LV_0` is negative,
        // which the unsigned `lv` column stores as 0, and char_mmo_char_fromsql + pc_validate_skill drop a level 0
        return None;
    }
    let lv = if s.flag == SKILL_FLAG_PERMANENT || s.flag == SKILL_FLAG_PERM_GRANTED { s.lv } else { s.flag.wrapping_sub(SKILL_FLAG_REPLACED_LV_0) };
    // other flags do not need to be saved
    let flag = if s.flag == SKILL_FLAG_PERM_GRANTED { SKILL_FLAG_PERM_GRANTED } else { SKILL_FLAG_PERMANENT };
    Some(Slot { id: skill_id, lv, flag })
}

/// pc.cpp pc_clean_skilltree: make sure all the skills are in the correct condition before persisting.
fn pc_clean_skilltree(s: &mut Skills) {
    for idx in s.slots.keys().copied().collect::<Vec<u16>>() {
        let mut sl = s.at(idx);
        if sl.flag == SKILL_FLAG_TEMPORARY || sl.flag == SKILL_FLAG_PLAGIARIZED {
            sl = Slot::default();
        } else if sl.flag >= SKILL_FLAG_REPLACED_LV_0 {
            sl.lv = sl.flag - SKILL_FLAG_REPLACED_LV_0;
            sl.flag = SKILL_FLAG_PERMANENT;
        }
        s.put(idx, sl);
    }
}

/// map.cpp map_quit's skill part: pc_clean_skilltree, then chrif_save writes the slots the char server stores.
pub fn map_quit(c: &Ctx, aid: u32) {
    let Some(mut s) = Skills::load(c, aid) else { return };
    pc_clean_skilltree(&mut s);
    for idx in s.slots.keys().copied().collect::<Vec<u16>>() {
        let skill_id = idx_skill(idx);
        match saved_form(s.at(idx), skill_id) {
            Some(sl) => s.put(idx, sl),
            None => s.put(idx, Slot::default()),
        }
    }
    s.save(c);
}

/// unit_free: the runtime row goes with the session.
pub fn unit_free(c: &Ctx, aid: u32) {
    c.db().pc_skilltree().account_id().delete(aid);
}

/// char.cpp char_mmo_char_fromsql (the `skill` rows) and pc.cpp pc_validate_skill, where pc_authok calls it: the
/// slots come back as the char server stored them (see the module doc); level 0 and unknown skills are removed; every
/// loaded skill has its id set.
pub fn pc_validate_skill(c: &Ctx, aid: u32) {
    let Some(p) = crate::pc::get(c, aid) else { return };
    let char_id = p.char_id;
    if c.db().pc_skilltree().account_id().find(aid).is_some() {
        c.db().pc_skilltree().account_id().delete(aid);
    }
    if let Some(old) = c.db().pc_skilltree().char_id().find(char_id) {
        c.db().pc_skilltree().account_id().delete(old.account_id);
    }
    let rows: Vec<CharSkill> = c.db().char_skill().by_char().filter(char_id).collect();
    let mut known: Vec<(u16, u16)> = Vec::new();
    for r in rows {
        let idx = index_silent(r.skill_id);
        // what the char server stored (a row of a character that left through map_quit is already in that form)
        let stored = saved_form(Slot { id: r.skill_id, lv: r.lv, flag: r.flag }, r.skill_id);
        let Some(sl) = stored else {
            c.db().char_skill().id().delete(r.id);
            continue;
        };
        if sl.id == 0 || sl.lv == 0 {
            c.db().char_skill().id().delete(r.id);
            continue;
        }
        if idx == 0 {
            log::warn!("pc_validate_skill: Removing invalid skill '{}' from player (AID={} CID={}).", r.skill_id, aid, char_id);
            c.db().char_skill().id().delete(r.id);
            continue;
        }
        if known.iter().any(|&(_, id)| id == r.skill_id) {
            c.db().char_skill().id().delete(r.id); // (a duplicate row: the table's primary key allows one)
            continue;
        }
        if sl.lv != r.lv || sl.flag != r.flag {
            c.db().char_skill().id().update(CharSkill { lv: sl.lv, flag: sl.flag, ..r });
        }
        known.push((idx, sl.id));
    }
    known.sort_unstable();
    c.db().pc_skilltree().insert(PcSkilltree { account_id: aid, char_id, known: known.into_iter().map(|(_, id)| id).collect(), ..Default::default() });
}

/// pc.cpp pc_reg_received's skill part: the job change levels and the plagiarised / reproduced skill come back from
/// the character's registers.
pub fn pc_reg_received(c: &Ctx, aid: u32) {
    let Some(mut s) = Skills::load(c, aid) else { return };
    s.meta.change_level_2nd = read_char_reg(c, aid, JOBCHANGE2ND_VAR) as u8;
    s.meta.change_level_3rd = read_char_reg(c, aid, JOBCHANGE3RD_VAR) as u8;
    s.meta.change_level_4th = read_char_reg(c, aid, JOBCHANGE4TH_VAR) as u8;
    let i = s.checkskill(sk::RG_PLAGIARISM);
    if i > 0 {
        let skid = read_char_reg(c, aid, SKILL_VAR_PLAGIARISM) as u16;
        s.meta.cloneskill_idx = sk::skill_get_index(skid);
        if s.meta.cloneskill_idx > 0 {
            let mut lv = read_char_reg(c, aid, SKILL_VAR_PLAGIARISM_LV) as u8;
            if lv > i {
                lv = i;
            }
            s.put(s.meta.cloneskill_idx, Slot { id: skid, lv, flag: SKILL_FLAG_PLAGIARIZED });
        }
    }
    let i = s.checkskill(sk::SC_REPRODUCE);
    if i > 0 {
        let skid = read_char_reg(c, aid, SKILL_VAR_REPRODUCE) as u16;
        s.meta.reproduceskill_idx = sk::skill_get_index(skid);
        if s.meta.reproduceskill_idx > 0 {
            let mut lv = read_char_reg(c, aid, SKILL_VAR_REPRODUCE_LV) as u8;
            if i < lv {
                lv = i;
            }
            s.put(s.meta.reproduceskill_idx, Slot { id: skid, lv, flag: SKILL_FLAG_PLAGIARIZED });
        }
    }
    s.save(c);
}

// ---------------------------------------------------------------------------------------------- status_calc_pc hooks

/// status_calc_pc_sub's `b_skill`: the slots before the recalculation.
pub fn snapshot(c: &Ctx, aid: u32) -> Vec<(u16, Slot)> {
    Skills::load(c, aid).map(|s| s.snapshot()).unwrap_or_default()
}

/// status_calc_pc_sub's client-side refresh: when the slots changed, the skills that left the tree are deleted one
/// by one (PACKETVER_RE_NUM >= 20190807: the client keeps them otherwise), then the whole list is sent.
pub fn calc_refresh(c: &Ctx, aid: u32, b_skill: &[(u16, Slot)]) {
    let now = snapshot(c, aid);
    if now.as_slice() == b_skill {
        return;
    }
    let after: BTreeMap<u16, Slot> = now.into_iter().collect();
    for &(idx, b) in b_skill {
        if b.id != 0 && after.get(&idx).map(|s| s.id).unwrap_or(0) == 0 {
            crate::clif_skill::clif_deleteskill(c, aid, b.id, true);
        }
    }
    crate::clif_skill::clif_skillinfoblock(c, aid);
}

// ---------------------------------------------------------------------------------------------- lent skills

/// A skill level lent for the length of one call (atcommand.cpp MC_CART_MDFY: `sd->status.skill[idx] = { id, lv,
/// SKILL_FLAG_TEMPORARY }`). Returns the `char_skill` row that holds it.
pub fn lend_skill(c: &Ctx, aid: u32, skill_id: u16, lv: u8) -> Option<u64> {
    let mut s = Skills::load(c, aid)?;
    let idx = sk::skill_get_index(skill_id);
    if idx == 0 {
        return None;
    }
    s.put(idx, Slot { id: skill_id, lv, flag: SKILL_FLAG_TEMPORARY });
    s.save(c);
    c.db().char_skill().by_char().filter(s.char_id).find(|r| r.skill_id == skill_id).map(|r| r.id)
}

/// Ends a [`lend_skill`]: the slot goes back to { 0, 0, SKILL_FLAG_PERMANENT } (id included, as MC_CART_MDFY(idx, 0)).
pub fn unlend_skill(c: &Ctx, row_id: u64) {
    let Some(r) = c.db().char_skill().id().find(row_id) else {
        return; // (a recalculation in between already emptied the slot)
    };
    let Some(p) = c.db().pc().char_id().find(r.char_id) else {
        c.db().char_skill().id().delete(row_id);
        return;
    };
    let Some(mut s) = Skills::load(c, p.account_id) else { return };
    let idx = index_silent(r.skill_id);
    s.put(idx, Slot::default());
    s.save(c);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn saved_form_matches_char_tosql() {
        let s = |id, lv, flag| Slot { id, lv, flag };
        // not in the tree: not saved
        assert_eq!(saved_form(s(0, 5, 0), 5), None);
        // temporary: not saved
        assert_eq!(saved_form(s(5, 5, SKILL_FLAG_TEMPORARY), 5), None);
        // level 0 permanent / granted: not saved
        assert_eq!(saved_form(s(5, 0, 0), 5), None);
        assert_eq!(saved_form(s(5, 0, SKILL_FLAG_PERM_GRANTED), 5), None);
        // replaced level 0: not saved; replaced level 3: saved as 3
        assert_eq!(saved_form(s(5, 7, SKILL_FLAG_REPLACED_LV_0), 5), None);
        assert_eq!(saved_form(s(5, 7, SKILL_FLAG_REPLACED_LV_0 + 3), 5), Some(s(5, 3, 0)));
        assert_eq!(saved_form(s(5, 4, SKILL_FLAG_PERM_GRANTED), 5), Some(s(5, 4, SKILL_FLAG_PERM_GRANTED)));
        assert_eq!(saved_form(s(5, 4, 0), 5), Some(s(5, 4, 0)));
    }

    #[test]
    fn changesex_ranges() {
        assert!(skill_ischangesex(sk::BD_ADAPTATION));
        assert!(skill_ischangesex(sk::DC_SERVICEFORYOU));
        assert!(!skill_ischangesex(sk::NV_BASIC));
    }

    #[test]
    fn index_table_inverts_skill_get_index() {
        for s in skill_db().iter().take(200) {
            assert_eq!(idx_skill(s.index), s.nameid);
        }
        assert_eq!(idx_skill(0), 0);
    }
}
