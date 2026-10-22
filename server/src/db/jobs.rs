//! pc.cpp's job and character-progression databases (rAthena e985006, RENEWAL): JobDatabase (db/job_stats.yml →
//! re/job_stats, job_aspd, job_exp, job_basepoints, job_outfits, import/job_stats), SkillTreeDatabase
//! (re/skill_tree.yml, Inherit applied), PlayerStatPointDatabase (re/statpoint.yml + computed levels), PenaltyDatabase
//! (re/level_penalty.yml), AttendanceDatabase (re/attendance.yml + Loki's import) and ReputationDatabase — loaded by
//! tools/datagen_mods/jobs.py with rAthena's load rules and loadingFinished steps (HP/SP/AP tables filled by
//! calc_basehp/sp/ap, cumulative job bonuses, max_param from battle_config, statpoints for unlisted levels, skill tree
//! inheritance). Lists rAthena keeps in an unordered_map are in libstdc++'s iteration order.
//!
//! Also the job helpers of pc.cpp / pc.hpp: pc_class2idx, pcdb_checkid, pc_jobid2mapid / pc_mapid2jobid, job_name, the
//! exp-table queries (pc_nextbaseexp …) and the class-predicate macros.

pub mod ids;

use std::collections::{BTreeMap, HashMap};

use serde::Deserialize;

pub use ids::*;

/// map.hpp MAX_LEVEL.
pub const MAX_LEVEL: usize = 275;
/// pc.hpp e_params PARAM_MAX (Str Agi Vit Int Dex Luk Pow Sta Wis Spl Con Crt).
pub const PARAM_MAX: usize = 12;
pub const PARAM_STR: usize = 0;
pub const PARAM_AGI: usize = 1;
pub const PARAM_VIT: usize = 2;
pub const PARAM_INT: usize = 3;
pub const PARAM_DEX: usize = 4;
pub const PARAM_LUK: usize = 5;
pub const PARAM_POW: usize = 6;
pub const PARAM_STA: usize = 7;
pub const PARAM_WIS: usize = 8;
pub const PARAM_SPL: usize = 9;
pub const PARAM_CON: usize = 10;
pub const PARAM_CRT: usize = 11;
/// config/const.hpp: the exp limit shown at the maximum level.
pub const MAX_LEVEL_BASE_EXP: u64 = 99_999_999;
pub const MAX_LEVEL_JOB_EXP: u64 = 999_999_999;
/// pc.hpp e_penalty_type.
pub const PENALTY_NONE: u16 = 0;
pub const PENALTY_EXP: u16 = 1;
pub const PENALTY_DROP: u16 = 2;
pub const PENALTY_MVP_EXP: u16 = 3;
pub const PENALTY_MVP_DROP: u16 = 4;
/// mmo.hpp MAX_AMOUNT (attendance reward cap).
pub const MAX_AMOUNT: u16 = 30000;

// ---------------------------------------------------------------------------------------------- class helpers

/// pc.cpp pc_class2idx: class id → index for CLASS_COUNT arrays (no validity check).
pub fn pc_class2idx(class_: i32) -> i32 {
    if class_ >= JOB_NOVICE_HIGH {
        return class_ - JOB_NOVICE_HIGH + JOB_MAX_BASIC;
    }
    class_
}

/// pc.hpp pcdb_checkid: is this a player class? (the class is compared as uint32)
pub fn pcdb_checkid(class_: i32) -> bool {
    let c = class_ as u32 as i64;
    let r = |a: i32, b: i32| c >= a as i64 && c <= b as i64;
    c < JOB_MAX_BASIC as i64
        || r(JOB_NOVICE_HIGH, JOB_DARK_COLLECTOR)
        || r(JOB_RUNE_KNIGHT, JOB_MECHANIC_T2)
        || r(JOB_BABY_RUNE_KNIGHT, JOB_BABY_MECHANIC2)
        || r(JOB_SUPER_NOVICE_E, JOB_SUPER_BABY_E)
        || r(JOB_KAGEROU, JOB_OBORO)
        || c == JOB_REBELLION as i64
        || c == JOB_SUMMONER as i64
        || c == JOB_BABY_SUMMONER as i64
        || r(JOB_BABY_NINJA, JOB_BABY_REBELLION)
        || r(JOB_BABY_STAR_GLADIATOR2, JOB_BABY_STAR_EMPEROR2)
        || r(JOB_DRAGON_KNIGHT, JOB_TROUVERE)
        || r(JOB_WINDHAWK2, JOB_IMPERIAL_GUARD2)
        || r(JOB_SKY_EMPEROR, JOB_SPIRIT_HANDLER)
        || c == JOB_SKY_EMPEROR2 as i64
}

/// pc.cpp job_name (map_msg.conf text).
pub fn job_name(class_: i32) -> &'static str {
    crate::data::msg_txt(job_name_msg(class_))
}

/// pc.hpp pc_is_primary_third.
pub fn pc_is_primary_third(class_: u64) -> bool {
    let c = class_ & MAPID_THIRDMASK;
    (MAPID_RUNE_KNIGHT..=MAPID_GUILLOTINE_CROSS).contains(&c) || (MAPID_ROYAL_GUARD..=MAPID_SHADOW_CHASER).contains(&c)
}
/// pc.hpp pc_is_primary_fourth.
pub fn pc_is_primary_fourth(class_: u64) -> bool {
    let c = class_ & MAPID_FOURTHMASK;
    (MAPID_DRAGON_KNIGHT..=MAPID_SHADOW_CROSS).contains(&c) || (MAPID_IMPERIAL_GUARD..=MAPID_ABYSS_CHASER).contains(&c)
}
/// pc.hpp pc_is_upper_expanded_first.
pub fn pc_is_upper_expanded_first(class_: u64) -> bool {
    class_ & MAPID_THIRDMASK == MAPID_SUPER_NOVICE_E
        || class_ & MAPID_THIRDMASK == MAPID_STAR_EMPEROR
        || class_ & MAPID_THIRDMASK == MAPID_SOUL_REAPER
        || class_ & MAPID_SECONDMASK == MAPID_REBELLION
        || class_ & MAPID_SECONDMASK == MAPID_KAGEROUOBORO
        || class_ & MAPID_FIRSTMASK == MAPID_SUMMONER
}
/// pc.hpp pc_is_upper_expanded_second.
pub fn pc_is_upper_expanded_second(class_: u64) -> bool {
    class_ & MAPID_FOURTHMASK == MAPID_HYPER_NOVICE
        || class_ & MAPID_FOURTHMASK == MAPID_SKY_EMPEROR
        || class_ & MAPID_FOURTHMASK == MAPID_SOUL_ASCETIC
        || class_ & MAPID_THIRDMASK == MAPID_NIGHT_WATCH
        || class_ & MAPID_THIRDMASK == MAPID_SHINKIROSHIRANUI
        || class_ & MAPID_SECONDMASK == MAPID_SPIRIT_HANDLER
}
/// pc.hpp pc_is_renewal_job.
pub fn pc_is_renewal_job(class_: u64) -> bool {
    pc_is_primary_third(class_) || pc_is_upper_expanded_first(class_)
}
/// pc.hpp pc_is_trait_job.
pub fn pc_is_trait_job(class_: u64) -> bool {
    pc_is_primary_fourth(class_) || pc_is_upper_expanded_second(class_)
}

// ---------------------------------------------------------------------------------------------- JobDatabase

/// pc.hpp s_job_info after JobDatabase::loadingFinished.
#[derive(Deserialize, Clone, Debug)]
pub struct JobInfo {
    /// Jobs: key (JOB_* constant).
    pub job_id: u16,
    /// BaseHp (Hp per Level, levels above MaxBaseLevel ignored); zeroes filled by calc_basehp. Length MaxBaseLevel
    /// (MAX_LEVEL for the classes loadingFinished skips).
    pub base_hp: Vec<u32>,
    /// BaseSp (Sp per Level); zeroes filled by calc_basesp.
    pub base_sp: Vec<u32>,
    /// BaseAp (Ap per Level); zeroes filled by calc_baseap.
    pub base_ap: Vec<u32>,
    /// HpFactor (default 0).
    pub hp_factor: u32,
    /// HpIncrease (default 500).
    pub hp_increase: u32,
    /// SpFactor (default 0).
    pub sp_factor: u32,
    /// SpIncrease (default 100).
    pub sp_increase: u32,
    /// ApFactor (default 0).
    pub ap_factor: u32,
    /// ApIncrease (default 0).
    pub ap_increase: u32,
    /// MaxWeight (default 20000).
    pub max_weight_base: u32,
    /// BonusStats (per job Level, Str..Crt), as running totals up to MaxJobLevel (entry i = job level i + 1). uint16
    /// as in rAthena: a negative total is stored wrapped.
    pub job_bonus: Vec<[u16; PARAM_MAX]>,
    /// BaseASPD per W_* (RENEWAL: MAX_WEAPON_TYPE + 1 entries, the last for shields; default 2000 / 10 = 200).
    pub aspd_base: Vec<i16>,
    /// BaseExp (Exp per Level; MAX_LEVEL entries, entry i = exp to leave level i + 1; 0 where not given).
    pub base_exp: Vec<u64>,
    /// JobExp (Exp per Level; MAX_LEVEL entries).
    pub job_exp: Vec<u64>,
    /// MaxBaseLevel (default MAX_LEVEL; 0 or above MAX_LEVEL is capped to MAX_LEVEL).
    pub max_base_level: u16,
    /// MaxJobLevel (default MAX_LEVEL).
    pub max_job_level: u16,
    /// MaxStats per PARAM_*; unset ones are battle_config's limit for the class (player.conf max_*_parameter).
    pub max_param: [u16; PARAM_MAX],
    /// AlternateOutfits (job_outfits.yml): classes whose outfit this class may use.
    pub alternate_outfits: Vec<u16>,
}

#[derive(Deserialize)]
struct JobFile {
    jobs: Vec<JobInfo>,
    loaded: Vec<(String, u32)>,
}

/// pc.cpp JobDatabase (job_db); `list` in iteration order.
pub struct JobDatabase {
    pub list: Vec<JobInfo>,
    by_id: HashMap<u16, usize>,
    /// (file, Body entries) as rAthena's "Done reading" lines report them.
    pub loaded: Vec<(String, u32)>,
}

impl<'de> Deserialize<'de> for JobDatabase {
    fn deserialize<D: serde::Deserializer<'de>>(d: D) -> Result<Self, D::Error> {
        let f = JobFile::deserialize(d)?;
        let by_id = f.jobs.iter().enumerate().map(|(i, j)| (j.job_id, i)).collect();
        Ok(JobDatabase { list: f.jobs, by_id, loaded: f.loaded })
    }
}

impl JobDatabase {
    pub fn find(&self, job_id: u16) -> Option<&JobInfo> {
        self.by_id.get(&job_id).map(|&i| &self.list[i])
    }
    /// JobDatabase::get_maxBaseLv (0 for an unknown class).
    pub fn get_max_base_lv(&self, job_id: u16) -> u32 {
        self.find(job_id).map(|j| j.max_base_level as u32).unwrap_or(0)
    }
    /// JobDatabase::get_maxJobLv.
    pub fn get_max_job_lv(&self, job_id: u16) -> u32 {
        self.find(job_id).map(|j| j.max_job_level as u32).unwrap_or(0)
    }
    /// JobDatabase::get_baseExp: base_exp[level - 1] (rAthena reads out of bounds for level 0 / above MAX_LEVEL; 0 here).
    pub fn get_base_exp(&self, job_id: u16, level: u32) -> u64 {
        self.find(job_id).and_then(|j| j.base_exp.get((level as usize).wrapping_sub(1)).copied()).unwrap_or(0)
    }
    /// JobDatabase::get_jobExp.
    pub fn get_job_exp(&self, job_id: u16, level: u32) -> u64 {
        self.find(job_id).and_then(|j| j.job_exp.get((level as usize).wrapping_sub(1)).copied()).unwrap_or(0)
    }
    /// JobDatabase::get_maxWeight.
    pub fn get_max_weight(&self, job_id: u16) -> i32 {
        self.find(job_id).map(|j| j.max_weight_base as i32).unwrap_or(0)
    }
}

crate::dataset!(job_db, "jobs_db", JobDatabase);

/// pc.cpp pc_maxbaselv (class = status.class_).
pub fn pc_maxbaselv(class_: u16) -> u32 {
    job_db().get_max_base_lv(class_)
}
/// pc.cpp pc_maxjoblv.
pub fn pc_maxjoblv(class_: u16) -> u32 {
    job_db().get_max_job_lv(class_)
}
/// pc.cpp pc_is_maxbaselv.
pub fn pc_is_maxbaselv(class_: u16, base_level: u32) -> bool {
    base_level >= pc_maxbaselv(class_)
}
/// pc.cpp pc_is_maxjoblv.
pub fn pc_is_maxjoblv(class_: u16, job_level: u32) -> bool {
    job_level >= pc_maxjoblv(class_)
}
/// pc.cpp pc_nextbaseexp: exp needed for the next base level (MAX_LEVEL_BASE_EXP at the maximum, 0 at level 0).
pub fn pc_nextbaseexp(class_: u16, base_level: u32) -> u64 {
    if base_level == 0 {
        return 0;
    }
    if pc_is_maxbaselv(class_, base_level) {
        return MAX_LEVEL_BASE_EXP;
    }
    job_db().get_base_exp(class_, base_level)
}
/// pc.cpp pc_nextjobexp: exp needed for the next job level (MAX_LEVEL_JOB_EXP at the maximum, 0 at level 0).
pub fn pc_nextjobexp(class_: u16, job_level: u32) -> u64 {
    if job_level == 0 {
        return 0;
    }
    if pc_is_maxjoblv(class_, job_level) {
        return MAX_LEVEL_JOB_EXP;
    }
    job_db().get_job_exp(class_, job_level)
}
/// pc.cpp pc_maxparameter: the class's stat limit (`class_mapid` = sd->class_, `sex` = status.sex). 0 for PARAM_MAX
/// or a class without data.
pub fn pc_maxparameter(class_mapid: u64, sex: i32, param: usize) -> u16 {
    let job = pc_mapid2jobid(class_mapid, sex);
    match job_db().find(job as u16) {
        Some(j) if param < PARAM_MAX => j.max_param[param],
        _ => 0,
    }
}

// ---------------------------------------------------------------------------------------------- SkillTreeDatabase

/// pc.hpp s_skill_tree_entry.
#[derive(Deserialize, Clone, Debug)]
pub struct SkillTreeEntry {
    /// Tree Name.
    pub skill_id: u16,
    /// Tree MaxLevel (capped to the skill's MaxLevel; entries with 0 are removed after inheritance).
    pub max_lv: u16,
    /// Tree BaseLevel (capped to the job's MaxBaseLevel; default 0).
    pub baselv: u32,
    /// Tree JobLevel (capped to the job's MaxJobLevel; default 0).
    pub joblv: u32,
    /// Tree Requires: (skill id, level), in the unordered_map's iteration order; Level 0 removes a requirement.
    pub need: Vec<(u16, u16)>,
    /// Tree Exclude: not passed on to inheriting jobs (default false).
    pub exclude_inherit: bool,
}

#[derive(Deserialize)]
struct SkillTreeRaw {
    job_id: u16,
    inherit_job: Vec<u16>,
    skills: Vec<SkillTreeEntry>,
}

/// pc.hpp s_skill_tree after SkillTreeDatabase::loadingFinished (inherited entries merged in).
pub struct SkillTree {
    pub job_id: u16,
    /// Inherit: the jobs whose trees were merged (in the order listed).
    pub inherit_job: Vec<u16>,
    /// skill_id → entry, in the unordered_map's iteration order (pc_calc_skilltree's loop order).
    pub skills: Vec<SkillTreeEntry>,
    index: HashMap<u16, usize>,
}

impl SkillTree {
    pub fn get(&self, skill_id: u16) -> Option<&SkillTreeEntry> {
        self.index.get(&skill_id).map(|&i| &self.skills[i])
    }
}

#[derive(Deserialize)]
struct SkillTreeFile {
    trees: Vec<SkillTreeRaw>,
    loaded: Vec<(String, u32)>,
}

/// pc.cpp SkillTreeDatabase (skill_tree_db); `list` in iteration order.
pub struct SkillTreeDatabase {
    pub list: Vec<SkillTree>,
    by_job: HashMap<u16, usize>,
    /// (file, entries) as rAthena's "Done reading" lines report them.
    pub loaded: Vec<(String, u32)>,
}

impl<'de> Deserialize<'de> for SkillTreeDatabase {
    fn deserialize<D: serde::Deserializer<'de>>(d: D) -> Result<Self, D::Error> {
        let f = SkillTreeFile::deserialize(d)?;
        let list: Vec<SkillTree> = f
            .trees
            .into_iter()
            .map(|t| {
                let index = t.skills.iter().enumerate().map(|(i, e)| (e.skill_id, i)).collect();
                SkillTree { job_id: t.job_id, inherit_job: t.inherit_job, skills: t.skills, index }
            })
            .collect();
        let by_job = list.iter().enumerate().map(|(i, t)| (t.job_id, i)).collect();
        Ok(SkillTreeDatabase { list, by_job, loaded: f.loaded })
    }
}

impl SkillTreeDatabase {
    pub fn find(&self, class_: u16) -> Option<&SkillTree> {
        self.by_job.get(&class_).map(|&i| &self.list[i])
    }
    /// SkillTreeDatabase::get_skill_data.
    pub fn get_skill_data(&self, class_: i32, skill_id: u16) -> Option<&SkillTreeEntry> {
        self.find(class_ as u16)?.get(skill_id)
    }
}

crate::dataset!(skill_tree_db, "jobs_skill_tree", SkillTreeDatabase);

// ---------------------------------------------------------------------------------------------- PlayerStatPointDatabase

#[derive(Deserialize)]
struct StatPointFile {
    /// Levels 1..=MAX_LEVEL: (statpoints, traitpoints).
    levels: Vec<(u32, u32)>,
    loaded: Vec<(String, u32)>,
}

/// pc.cpp PlayerStatPointDatabase (statpoint_db) after loadingFinished: every level 1..=MAX_LEVEL has an entry (level 1
/// = inter_athena.conf start_status_points; unlisted levels computed).
pub struct PlayerStatPointDatabase {
    levels: Vec<(u32, u32)>,
    /// (file, entries) as rAthena's "Done reading" lines report them.
    pub loaded: Vec<(String, u32)>,
}

impl<'de> Deserialize<'de> for PlayerStatPointDatabase {
    fn deserialize<D: serde::Deserializer<'de>>(d: D) -> Result<Self, D::Error> {
        let f = StatPointFile::deserialize(d)?;
        Ok(PlayerStatPointDatabase { levels: f.levels, loaded: f.loaded })
    }
}

impl PlayerStatPointDatabase {
    fn find(&self, level: u16) -> Option<(u32, u32)> {
        if level == 0 {
            return None;
        }
        self.levels.get(level as usize - 1).copied()
    }
    /// get_table_point: total status points at a base level (0 without an entry).
    pub fn get_table_point(&self, level: u16) -> u32 {
        self.find(level).map(|e| e.0).unwrap_or(0)
    }
    /// pc_gets_status_point: points gained going from `level` to `level + 1`.
    pub fn pc_gets_status_point(&self, level: u16) -> u32 {
        let next = self.get_table_point(level.wrapping_add(1));
        let cur = self.get_table_point(level);
        if next > cur {
            next - cur
        } else {
            0
        }
    }
    /// get_trait_table_point.
    pub fn get_trait_table_point(&self, level: u16) -> u32 {
        self.find(level).map(|e| e.1).unwrap_or(0)
    }
    /// pc_gets_trait_point.
    pub fn pc_gets_trait_point(&self, level: u16) -> u32 {
        let next = self.get_trait_table_point(level.wrapping_add(1));
        let cur = self.get_trait_table_point(level);
        if next > cur {
            next - cur
        } else {
            0
        }
    }
}

crate::dataset!(statpoint_db, "jobs_statpoint", PlayerStatPointDatabase);

// ---------------------------------------------------------------------------------------------- penalty, attendance, reputation

/// pc.hpp s_penalty after PenaltyDatabase::loadingFinished.
#[derive(Deserialize, Clone, Debug)]
pub struct Penalty {
    /// Type: PENALTY_*.
    #[serde(rename = "type")]
    pub type_: u16,
    /// rate[difference + MAX_LEVEL - 1] for monster level − player level in −274..=274 (Rate; gaps take the nearest
    /// listed rate going away from 0, 100 before the first).
    pub rate: Vec<u16>,
}

/// One attendance.yml Body node as parseBodyNode reads it (`bad`: the read fails and rAthena `continue`s / returns).
#[derive(Deserialize, Clone, Debug)]
pub struct AttendanceValue {
    pub bad: bool,
    pub value: u32,
}

#[derive(Deserialize, Clone, Debug)]
pub struct AttendanceRewardNode {
    /// Day.
    pub day: AttendanceValue,
    /// ItemId (absent: None; `bad` also when the item is not in item_db).
    pub item: Option<AttendanceValue>,
    /// Amount.
    pub amount: Option<AttendanceValue>,
}

#[derive(Deserialize, Clone, Debug)]
pub struct AttendanceNode {
    /// Start (YYYYMMDD).
    pub start: u32,
    /// End (YYYYMMDD).
    pub end: Option<AttendanceValue>,
    /// Rewards.
    pub rewards: Option<Vec<AttendanceRewardNode>>,
}

/// pc.hpp s_reputation (map-server build: no Visibility).
#[derive(Deserialize, Clone, Debug)]
pub struct Reputation {
    /// Id.
    pub id: i64,
    /// Name.
    pub name: String,
    /// Variable (character variable holding the points; at most 32 characters).
    pub variable: String,
    /// Minimum (default INT64_MIN).
    pub minimum: i64,
    /// Maximum (default INT64_MIN, as rAthena sets it).
    pub maximum: i64,
}

#[derive(Deserialize)]
pub struct JobMisc {
    /// PenaltyDatabase in iteration order.
    pub penalty: Vec<Penalty>,
    /// attendance.yml then db/import/attendance.yml Body nodes, in load order.
    pub attendance_nodes: Vec<AttendanceNode>,
    /// ReputationDatabase in iteration order.
    pub reputation: Vec<Reputation>,
    /// (file, entries) as rAthena's "Done reading" lines report them (attendance: nodes with a readable Start).
    pub loaded: Vec<(String, u32)>,
}

crate::dataset!(job_misc, "jobs_misc", JobMisc);

/// The tail of pc.cpp pc_level_penalty_mod (RENEWAL_DROP / RENEWAL_EXP): the rate for `monster_level - base_level`,
/// 100 when the type has no table. rAthena indexes rate[] without a bounds check; a difference beyond ±274 reads
/// outside the array there (undefined) — it is clamped to the table's ends here.
pub fn pc_level_penalty_rate(type_: u16, level_difference: i32) -> u16 {
    match job_misc().penalty.iter().find(|p| p.type_ == type_) {
        Some(p) => {
            let i = (level_difference + MAX_LEVEL as i32 - 1).clamp(0, p.rate.len() as i32 - 1);
            p.rate[i as usize]
        }
        None => 100,
    }
}

/// pc.cpp ReputationDatabase::find.
pub fn reputation_find(id: i64) -> Option<&'static Reputation> {
    job_misc().reputation.iter().find(|r| r.id == id)
}

/// pc.hpp s_attendance_reward.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct AttendanceReward {
    pub item_id: u32,
    pub amount: u16,
}

/// pc.hpp s_attendance_period (rewards keyed by day - 1, a std::map).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct AttendancePeriod {
    pub start: u32,
    pub end: u32,
    pub rewards: BTreeMap<u32, AttendanceReward>,
}

/// pc.cpp AttendanceDatabase as loaded on `date` (YYYYMMDD, date_get(DT_YYYYMMDD) — the server's local date):
/// AttendanceDatabase::parseBodyNode replayed over the recorded nodes. rAthena runs it once at start-up; the module
/// has no start-up, so it is evaluated for the date asked (as if the server had started that day). The periods are
/// returned in the unordered_map's iteration order (libstdc++, keyed by Start).
pub fn attendance_db(date: u32) -> Vec<AttendancePeriod> {
    let mut db: Vec<AttendancePeriod> = Vec::new();
    for node in &job_misc().attendance_nodes {
        attendance_parse(&mut db, node, date);
    }
    let order = libstdcxx_unordered_order(&db.iter().map(|p| p.start as u64).collect::<Vec<_>>());
    order.into_iter().map(|k| db.iter().find(|p| p.start as u64 == k).cloned().unwrap_or_else(|| db[0].clone())).collect()
}

/// AttendanceDatabase::parseBodyNode. Changes to an existing period stay even when the node is then rejected (rAthena
/// edits the stored object in place).
fn attendance_parse(db: &mut Vec<AttendancePeriod>, node: &AttendanceNode, date: u32) -> bool {
    let start = node.start;
    let existing = db.iter().position(|p| p.start == start);
    let exists = existing.is_some();
    let mut period = match existing {
        Some(i) => db[i].clone(),
        None => {
            if node.end.is_none() || node.rewards.is_none() {
                return false; // "Node End/Rewards is missing"
            }
            AttendancePeriod { start, end: 0, rewards: BTreeMap::new() }
        }
    };
    let ok = attendance_parse_into(db, &mut period, node, date, exists);
    match existing {
        Some(i) => db[i] = period,
        None if ok => db.push(period),
        None => {}
    }
    ok
}

fn attendance_parse_into(db: &[AttendancePeriod], period: &mut AttendancePeriod, node: &AttendanceNode, date: u32, exists: bool) -> bool {
    let start = node.start;
    let mut requires_collision_detection = !exists;
    if let Some(end) = &node.end {
        if end.bad {
            return false;
        }
        // If the period is outdated already, it is not even parsed
        if end.value < date {
            return false;
        }
        if !exists || period.end != end.value {
            requires_collision_detection = true;
            period.end = end.value;
        }
    }
    if requires_collision_detection {
        for other in db {
            if exists && other.start == period.start {
                continue; // don't compare to yourself
            }
            // start inside another period
            if other.start <= period.start && start <= other.end {
                return false;
            }
            // end inside another period
            if other.start <= period.end && period.end <= other.end {
                return false;
            }
        }
    }
    if let Some(rewards) = &node.rewards {
        for r in rewards {
            if r.day.bad {
                continue;
            }
            let day = r.day.value.wrapping_sub(1);
            let reward_exists = period.rewards.contains_key(&day);
            if !reward_exists && r.item.is_none() {
                return false; // Node "ItemId" is missing
            }
            let mut reward = period.rewards.get(&day).cloned().unwrap_or(AttendanceReward { item_id: 0, amount: 0 });
            let mut store = true;
            if let Some(item) = &r.item {
                if item.bad {
                    store = false;
                }
                if store {
                    reward.item_id = item.value;
                }
            }
            if store {
                match &r.amount {
                    Some(a) if a.bad => store = false,
                    Some(a) => {
                        let mut amount = a.value as u16;
                        if amount == 0 {
                            amount = 1;
                        } else if amount > MAX_AMOUNT {
                            amount = MAX_AMOUNT;
                        }
                        reward.amount = amount;
                    }
                    None => {
                        if !reward_exists {
                            reward.amount = 1;
                        }
                    }
                }
            }
            // an existing reward keeps what was set before a `continue`; a new one is only stored when complete
            if reward_exists || store {
                period.rewards.insert(day, reward);
            }
        }
        for day in 0..period.rewards.len() as u32 {
            if !period.rewards.contains_key(&day) {
                return false; // "Reward for day %d is missing"
            }
        }
    }
    true
}

/// pc.cpp pc_attendance_period: the period running on `date` (first in iteration order).
pub fn pc_attendance_period(date: u32) -> Option<AttendancePeriod> {
    attendance_db(date).into_iter().find(|p| p.start <= date && p.end >= date)
}

/// Iteration order of a libstdc++ (GCC 13) std::unordered_map<integral, T> after inserting `keys` in order (duplicates
/// ignored): identity hash, prime rehash policy, max load factor 1 — the same model tools/datagen_mods/skills.py uses.
pub fn libstdcxx_unordered_order(keys: &[u64]) -> Vec<u64> {
    const FAST_BKT: [usize; 14] = [2, 2, 2, 3, 5, 5, 7, 7, 11, 11, 11, 11, 13, 13];
    const PRIMES: [usize; 40] = [
        17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71, 73, 79, 83, 89, 97, 103, 109, 113, 127, 137, 139, 149, 157,
        167, 179, 193, 199, 211, 227, 241, 257, 277, 293, 313, 337, 359,
    ];
    // node "before" markers: usize::MAX = _M_before_begin
    const BB: usize = usize::MAX;
    let mut nodes: Vec<u64> = Vec::new(); // node index -> key
    let mut next: Vec<Option<usize>> = Vec::new();
    let mut head: Option<usize> = None;
    let mut buckets: Vec<Option<usize>> = vec![None];
    let mut next_resize = 0usize;
    let bidx = |k: u64, n: usize| (k % n as u64) as usize;
    let next_bkt = |n: usize, next_resize: &mut usize| -> usize {
        if n < FAST_BKT.len() {
            if n == 0 {
                return 1;
            }
            *next_resize = FAST_BKT[n];
            return FAST_BKT[n];
        }
        let p = PRIMES.iter().copied().find(|&p| p >= n).unwrap_or(*PRIMES.last().unwrap_or(&359));
        *next_resize = p;
        p
    };
    for &k in keys {
        if nodes.contains(&k) {
            continue;
        }
        let n_elt = nodes.len();
        let nb = buckets.len();
        // _M_need_rehash(nb, n_elt, 1)
        if n_elt + 1 > next_resize {
            let min_bkts = (n_elt + 1).max(if next_resize != 0 { 0 } else { 11 });
            if min_bkts >= nb {
                let new_n = next_bkt((min_bkts + 1).max(nb * 2), &mut next_resize);
                // _M_rehash_aux
                let mut newb: Vec<Option<usize>> = vec![None; new_n];
                let mut p = head;
                head = None;
                let mut bbegin = 0usize;
                while let Some(pi) = p {
                    let nx = next[pi];
                    let b = bidx(nodes[pi], new_n);
                    match newb[b] {
                        None => {
                            next[pi] = head;
                            head = Some(pi);
                            newb[b] = Some(BB);
                            if next[pi].is_some() {
                                newb[bbegin] = Some(pi);
                            }
                            bbegin = b;
                        }
                        Some(q) => {
                            if q == BB {
                                next[pi] = head;
                                head = Some(pi);
                            } else {
                                next[pi] = next[q];
                                next[q] = Some(pi);
                            }
                        }
                    }
                    p = nx;
                }
                buckets = newb;
            } else {
                next_resize = nb;
            }
        }
        let nb = buckets.len();
        let ni = nodes.len();
        nodes.push(k);
        next.push(None);
        let b = bidx(k, nb);
        match buckets[b] {
            Some(q) => {
                if q == BB {
                    next[ni] = head;
                    head = Some(ni);
                } else {
                    next[ni] = next[q];
                    next[q] = Some(ni);
                }
            }
            None => {
                next[ni] = head;
                head = Some(ni);
                if let Some(nx) = next[ni] {
                    let nbk = bidx(nodes[nx], nb);
                    buckets[nbk] = Some(ni);
                }
                buckets[b] = Some(BB);
            }
        }
    }
    let mut out = Vec::with_capacity(nodes.len());
    let mut p = head;
    while let Some(pi) = p {
        out.push(nodes[pi]);
        p = next[pi];
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    /// The oracle's map.log "Done reading" lines (rAthena e985006 with Loki's imports).
    fn loaded(l: &[(String, u32)]) -> Vec<(&str, u32)> {
        l.iter().map(|(f, n)| (f.as_str(), *n)).collect()
    }

    #[test]
    fn counts_match_the_oracle() {
        assert_eq!(
            loaded(&job_db().loaded),
            vec![
                ("db/re/job_stats.yml", 83),
                ("db/re/job_aspd.yml", 43),
                ("db/re/job_exp.yml", 19),
                ("db/re/job_basepoints.yml", 89),
                ("db/re/job_outfits.yml", 13),
            ]
        );
        assert_eq!(job_db().list.len(), 175);
        assert_eq!(loaded(&skill_tree_db().loaded), vec![("db/re/skill_tree.yml", 175)]);
        assert_eq!(loaded(&statpoint_db().loaded), vec![("db/re/statpoint.yml", 275)]);
        assert_eq!(statpoint_db().levels.len(), MAX_LEVEL);
        assert_eq!(
            loaded(&job_misc().loaded),
            vec![
                ("db/re/level_penalty.yml", 2),
                ("db/re/attendance.yml", 0),
                ("db/import/attendance.yml", 1),
                ("db/re/reputation.yml", 4),
            ]
        );
        assert_eq!(job_misc().penalty.len(), 2);
        assert_eq!(job_misc().reputation.len(), 4);
        assert_eq!(attendance_db(20260927).len(), 1);
    }

    #[test]
    fn novice_and_swordman() {
        let n = job_db().find(JOB_NOVICE as u16).unwrap();
        assert_eq!((n.max_base_level, n.max_job_level, n.max_weight_base), (99, 10, 20000));
        assert_eq!(&n.base_hp[..5], &[40, 45, 50, 55, 60]);
        assert_eq!(n.base_hp[98], 530);
        assert_eq!(n.base_hp.len(), 99);
        assert_eq!(n.job_bonus.len(), 10);
        assert_eq!(n.job_bonus[9], [1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0]);
        assert_eq!(n.max_param, [99, 99, 99, 99, 99, 99, 0, 0, 0, 0, 0, 0]);
        assert_eq!(n.aspd_base.len(), 25);
        assert_eq!(n.aspd_base[0], 40); // Fist
        let s = job_db().find(JOB_SWORDMAN as u16).unwrap();
        assert_eq!((s.max_weight_base, s.hp_factor, s.sp_increase), (28000, 70, 200));
        assert_eq!(s.max_job_level, 50);
        assert_eq!(s.job_bonus[1][PARAM_STR], 1); // BonusStats Level 2: Str 1
        assert_eq!(s.job_bonus[49], [7, 2, 4, 0, 3, 2, 0, 0, 0, 0, 0, 0]);
        // Knight2 has no MAPID (pc_jobid2mapid gives every bit): rAthena gives it the baby-third stat limit
        assert_eq!(pc_jobid2mapid(JOB_KNIGHT2 as u16), u64::MAX);
        assert_eq!(job_db().find(JOB_KNIGHT2 as u16).unwrap().max_param[0], 117);
    }

    #[test]
    fn exp_tables() {
        let nov = JOB_NOVICE as u16;
        assert_eq!(pc_nextbaseexp(nov, 1), 548);
        assert_eq!(pc_nextbaseexp(nov, 99), MAX_LEVEL_BASE_EXP); // max level
        assert_eq!(pc_nextbaseexp(nov, 0), 0);
        assert_eq!(pc_nextjobexp(nov, 1), 10);
        let dk = JOB_DRAGON_KNIGHT as u16;
        assert_eq!(pc_maxbaselv(dk), 275);
        assert_eq!(pc_nextbaseexp(dk, 1), 658);
        assert_eq!(pc_nextbaseexp(dk, 99), 1272747);
        assert_eq!(pc_nextbaseexp(dk, 200), 653047446);
        assert_eq!(pc_nextbaseexp(dk, 275), MAX_LEVEL_BASE_EXP);
        assert_eq!(pc_maxjoblv(dk), 60);
    }

    #[test]
    fn statpoints() {
        let sp = statpoint_db();
        assert_eq!(sp.get_table_point(1), 48); // inter_athena.conf start_status_points
        assert_eq!(sp.get_table_point(2), 51);
        assert_eq!(sp.get_table_point(99), 1273);
        assert_eq!(sp.get_table_point(200), 4099);
        assert_eq!(sp.pc_gets_status_point(1), 3);
        assert_eq!(sp.pc_gets_status_point(275), 0);
        assert_eq!(sp.get_trait_table_point(200), 0);
        assert_eq!(sp.get_trait_table_point(201), 3);
        assert_eq!(sp.get_trait_table_point(275), 285);
        assert_eq!(sp.get_table_point(0), 0);
        assert_eq!(sp.get_table_point(276), 0);
    }

    #[test]
    fn skill_tree() {
        use crate::db::skills::*;
        // Knight inherits Swordman and Novice; Two-Hand Quicken needs Two-Handed Sword Mastery 1
        let t = skill_tree_db().find(JOB_KNIGHT as u16).unwrap();
        assert!(t.inherit_job.contains(&(JOB_SWORDMAN as u16)));
        assert!(t.get(NV_BASIC).is_some());
        assert!(t.get(SM_BASH).is_some());
        let q = t.get(KN_TWOHANDQUICKEN).unwrap();
        assert_eq!(q.max_lv, 10);
        assert_eq!(q.need, vec![(SM_TWOHAND, 1)]);
        assert_eq!(skill_tree_get_max(KN_TWOHANDQUICKEN, JOB_KNIGHT), 10);
        assert_eq!(skill_tree_get_max(AL_HEAL, JOB_KNIGHT), 10); // not in the tree: the skill's MaxLevel
    }

    #[test]
    fn helpers() {
        assert_eq!(pc_class2idx(JOB_NOVICE_HIGH), JOB_MAX_BASIC);
        assert!(pcdb_checkid(JOB_SUMMONER));
        assert!(!pcdb_checkid(JOB_MAX_BASIC));
        assert!(!pcdb_checkid(-1));
        assert_eq!(pc_jobid2mapid(JOB_KAGEROU as u16), MAPID_KAGEROUOBORO);
        assert_eq!(pc_mapid2jobid(MAPID_KAGEROUOBORO, 1), JOB_KAGEROU);
        assert_eq!(pc_mapid2jobid(MAPID_KAGEROUOBORO, 0), JOB_OBORO);
        assert_eq!(job_name_msg(JOB_NOVICE), 550);
        assert_eq!(job_name_msg(99999), 655);
        assert!(pc_is_trait_job(pc_jobid2mapid(JOB_DRAGON_KNIGHT as u16)));
        assert!(!pc_is_trait_job(pc_jobid2mapid(JOB_RUNE_KNIGHT as u16)));
    }

    #[test]
    fn penalty_and_reputation() {
        assert_eq!(pc_level_penalty_rate(PENALTY_EXP, 0), 100);
        assert_eq!(pc_level_penalty_rate(PENALTY_MVP_EXP, 0), 100); // no table
        assert_eq!(reputation_find(1).unwrap().variable, "RepPointsOrc");
    }

    #[test]
    fn attendance_periods() {
        // Loki's db/import/attendance.yml: 2026-09-01 .. 2026-12-31, 20 rewards
        let p = pc_attendance_period(20261001).unwrap();
        assert_eq!((p.start, p.end), (20260901, 20261231));
        // the oracle's attendance_db (read from the running map-server with gdb): (day - 1, item, amount)
        let want: [(u32, u32, u16); 20] = [
            (0, 12209, 3), (1, 22545, 18), (2, 23012, 18), (3, 12515, 18), (4, 23475, 18), (5, 17251, 1), (6, 13709, 1),
            (7, 17569, 1), (8, 12633, 18), (9, 17162, 1), (10, 12754, 50), (11, 12264, 18), (12, 12261, 18),
            (13, 12221, 1), (14, 6635, 2), (15, 7338, 50), (16, 12790, 1), (17, 14799, 1), (18, 25791, 1), (19, 20987, 1),
        ];
        let got: Vec<(u32, u32, u16)> = p.rewards.iter().map(|(d, r)| (*d, r.item_id, r.amount)).collect();
        assert_eq!(got, want.to_vec());
        assert!(pc_attendance_period(20260831).is_none());
        // a period whose End has passed on the load date is skipped
        assert!(attendance_db(20270101).is_empty());
    }

    #[test]
    fn unordered_order_model() {
        // Expected orders printed by std::unordered_map<uint64_t, int> built with g++ 13.3 (the oracle's compiler).
        let keys: Vec<u64> = (1..=20).collect();
        let want: Vec<u64> = vec![20, 19, 18, 17, 16, 15, 14, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13];
        assert_eq!(libstdcxx_unordered_order(&keys), want);
        let keys: Vec<u64> = vec![
            2652, 1235, 3234, 395, 593, 4389, 771, 2995, 4774, 475, 4156, 1758, 307, 704, 3552, 3425, 572, 1971, 743, 4514,
            3477, 484, 4632, 1014, 1828, 4775, 506, 4727, 4796, 3249, 406, 1811, 381, 4560, 1090, 2372, 3433, 1181, 4429, 964,
            4676, 2527, 4589, 1480, 844, 4764, 4679, 1539, 3050, 798, 4487, 514, 4623, 488, 1687, 4066, 4355, 3502, 2573, 3814,
        ];
        let want: Vec<u64> = vec![
            4727, 1828, 484, 307, 2372, 3477, 4775, 704, 4514, 4632, 3814, 3433, 1971, 572, 395, 3552, 3425, 593, 475, 4487,
            3234, 4355, 1014, 506, 2652, 4389, 771, 4429, 743, 3050, 4066, 4796, 4560, 1235, 4156, 406, 381, 2995, 3249, 1090,
            1181, 4774, 964, 4676, 2527, 4589, 1480, 1539, 844, 4764, 1687, 798, 514, 4623, 3502, 1758, 4679, 488, 1811, 2573,
        ];
        assert_eq!(libstdcxx_unordered_order(&keys), want);
    }

    /// Cross-check tool, not a regular test: `LOKI_JOB_HELPERS=<file> cargo test --lib dump_job_helpers -- --ignored`
    /// writes job_name / pc_jobid2mapid / pc_class2idx / pc_mapid2jobid over the class ranges, to compare with the
    /// oracle's own functions called through gdb.
    #[test]
    #[ignore]
    fn dump_job_helpers() {
        let Ok(path) = std::env::var("LOKI_JOB_HELPERS") else { return };
        let mut name = serde_json::Map::new();
        let mut j2m = serde_json::Map::new();
        let mut idx = serde_json::Map::new();
        let mut m2j = serde_json::Map::new();
        for c in (0..40).chain(3990..4400) {
            name.insert(c.to_string(), job_name(c).into());
            j2m.insert(c.to_string(), pc_jobid2mapid(c as u16).into());
            idx.insert(c.to_string(), pc_class2idx(c).into());
            let m = pc_jobid2mapid(c as u16);
            for sex in 0..2 {
                m2j.insert(format!("{m}_{sex}"), pc_mapid2jobid(m, sex).into());
            }
        }
        let out = serde_json::json!({"job_name": name, "jobid2mapid": j2m, "class2idx": idx, "mapid2jobid": m2j});
        std::fs::write(path, out.to_string()).unwrap();
    }
}
