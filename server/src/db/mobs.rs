//! map/mob.cpp static databases, as rAthena holds them once `mob_load()` has run (tools/datagen_mods/mobs.py resolves
//! the load rules): MobDatabase (db/re/mob_db.yml + import, parseBodyNode defaults, the battle_config rates applied at
//! load — exp rates, damage motion, HP rates, view/chase ranges —, loadingFinished's class modes and status_calc_misc),
//! drops after mob_drop_ratio_adjust, skills after mob_skill_db_set (db/re/mob_skill_db.txt + import), MobChatDatabase,
//! MobSummonDatabase, MapDropDatabase, and what mob_drop_ratio_adjust writes into the item database.
//!
//! Every list is in rAthena's iteration order (its std::unordered_map order, reproduced by the datagen step), so the
//! lookups that return the first match in iteration order (mobdb_search_aegisname, mobdb_searchname, umap_random over a
//! summon group) behave as rAthena's do.

use std::collections::HashMap;

use serde::Deserialize;

// ------------------------------------------------------------------------------------------------------ constants

/// mob.cpp MIN_MOB_DB / MAX_MOB_DB / MIN_MOB_DB2 / MAX_MOB_DB2: valid database ids are 1001..3998 and 20021..31998.
pub const MIN_MOB_DB: u32 = 1000;
pub const MAX_MOB_DB: u32 = 3999;
pub const MIN_MOB_DB2: u32 = 20020;
pub const MAX_MOB_DB2: u32 = 31999;
/// mob.cpp MOB_CLONE_START (MAX_MOB_DB) / MOB_CLONE_END (MIN_MOB_DB2): ids of player clones (created at run time).
pub const MOB_CLONE_START: i32 = 3999;
pub const MOB_CLONE_END: i32 = 20020;
/// mob.hpp MAX_MOB_DROP / MAX_MVP_DROP; map.hpp MAX_MOBSKILL; itemdb.hpp MAX_SEARCH.
pub const MAX_MOB_DROP: usize = 10;
pub const MAX_MVP_DROP: usize = 3;
pub const MAX_MOBSKILL: usize = 50;
pub const MAX_SEARCH: usize = 10;
/// mob.cpp MOB_MAX_DELAY (24 h).
pub const MOB_MAX_DELAY: i32 = 24 * 3600 * 1000;
/// mob.hpp enum MOBID (the ones the server code names).
pub const MOBID_PORING: u32 = 1002;
pub const MOBID_EMPERIUM: u32 = 1288;

/// mmo.hpp enum e_mode.
pub const MD_NONE: i32 = 0x0000000;
pub const MD_CANMOVE: i32 = 0x0000001;
pub const MD_LOOTER: i32 = 0x0000002;
pub const MD_AGGRESSIVE: i32 = 0x0000004;
pub const MD_ASSIST: i32 = 0x0000008;
pub const MD_CASTSENSORIDLE: i32 = 0x0000010;
pub const MD_NORANDOMWALK: i32 = 0x0000020;
pub const MD_NOCAST: i32 = 0x0000040;
pub const MD_CANATTACK: i32 = 0x0000080;
pub const MD_CASTSENSORCHASE: i32 = 0x0000200;
pub const MD_CHANGECHASE: i32 = 0x0000400;
pub const MD_ANGRY: i32 = 0x0000800;
pub const MD_CHANGETARGETMELEE: i32 = 0x0001000;
pub const MD_CHANGETARGETCHASE: i32 = 0x0002000;
pub const MD_TARGETWEAK: i32 = 0x0004000;
pub const MD_RANDOMTARGET: i32 = 0x0008000;
pub const MD_IGNOREMELEE: i32 = 0x0010000;
pub const MD_IGNOREMAGIC: i32 = 0x0020000;
pub const MD_IGNORERANGED: i32 = 0x0040000;
pub const MD_MVP: i32 = 0x0080000;
pub const MD_IGNOREMISC: i32 = 0x0100000;
pub const MD_KNOCKBACKIMMUNE: i32 = 0x0200000;
pub const MD_TELEPORTBLOCK: i32 = 0x0400000;
pub const MD_FIXEDITEMDROP: i32 = 0x1000000;
pub const MD_DETECTOR: i32 = 0x2000000;
pub const MD_STATUSIMMUNE: i32 = 0x4000000;
pub const MD_SKILLIMMUNE: i32 = 0x8000000;
/// mmo.hpp MD_MASK / ATR_MASK / CL_MASK.
pub const MD_MASK: i32 = 0x000FFFF;
pub const ATR_MASK: i32 = 0x0FF0000;
pub const CL_MASK: i32 = 0xF000000;

/// mob.hpp enum e_aegis_monsterclass.
pub const CLASS_NONE: i8 = -1;
pub const CLASS_NORMAL: u8 = 0;
pub const CLASS_BOSS: u8 = 1;
pub const CLASS_GUARDIAN: u8 = 2;
pub const CLASS_BATTLEFIELD: u8 = 4;
pub const CLASS_EVENT: u8 = 5;

/// mob.hpp enum e_mob_bosstype.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum BossType {
    None = 0,
    MiniBoss = 1,
    Mvp = 2,
}

/// mob.hpp enum MobSkillState.
pub const MSS_ANY: i32 = -1;
pub const MSS_IDLE: i32 = 0;
pub const MSS_WALK: i32 = 1;
pub const MSS_LOOT: i32 = 2;
pub const MSS_DEAD: i32 = 3;
pub const MSS_BERSERK: i32 = 4;
pub const MSS_ANGRY: i32 = 5;
pub const MSS_RUSH: i32 = 6;
pub const MSS_FOLLOW: i32 = 7;
pub const MSS_ANYTARGET: i32 = 8;

/// mob.hpp enum e_mob_skill_target.
pub const MST_TARGET: i16 = 0;
pub const MST_RANDOM: i16 = 1;
pub const MST_SELF: i16 = 2;
pub const MST_FRIEND: i16 = 3;
pub const MST_MASTER: i16 = 4;
pub const MST_AROUND5: i16 = 5;
pub const MST_AROUND6: i16 = 6;
pub const MST_AROUND7: i16 = 7;
pub const MST_AROUND8: i16 = 8;
pub const MST_AROUND1: i16 = 9;
pub const MST_AROUND2: i16 = 10;
pub const MST_AROUND3: i16 = 11;
pub const MST_AROUND4: i16 = 12;
pub const MST_AROUND: i16 = MST_AROUND4;

/// mob.hpp enum e_mob_skill_condition.
pub const MSC_ALWAYS: i16 = 0;
pub const MSC_MYHPLTMAXRATE: i16 = 1;
pub const MSC_MYHPINRATE: i16 = 2;
pub const MSC_FRIENDHPLTMAXRATE: i16 = 3;
pub const MSC_FRIENDHPINRATE: i16 = 4;
pub const MSC_MYSTATUSON: i16 = 5;
pub const MSC_MYSTATUSOFF: i16 = 6;
pub const MSC_FRIENDSTATUSON: i16 = 7;
pub const MSC_FRIENDSTATUSOFF: i16 = 8;
pub const MSC_ATTACKPCGT: i16 = 9;
pub const MSC_ATTACKPCGE: i16 = 10;
pub const MSC_SLAVELT: i16 = 11;
pub const MSC_SLAVELE: i16 = 12;
pub const MSC_CLOSEDATTACKED: i16 = 13;
pub const MSC_LONGRANGEATTACKED: i16 = 14;
pub const MSC_AFTERSKILL: i16 = 15;
pub const MSC_SKILLUSED: i16 = 16;
pub const MSC_CASTTARGETED: i16 = 17;
pub const MSC_RUDEATTACKED: i16 = 18;
pub const MSC_MASTERHPLTMAXRATE: i16 = 19;
pub const MSC_MASTERATTACKED: i16 = 20;
pub const MSC_ALCHEMIST: i16 = 21;
pub const MSC_SPAWN: i16 = 22;
pub const MSC_MOBNEARBYGT: i16 = 23;
pub const MSC_GROUNDATTACKED: i16 = 24;
pub const MSC_DAMAGEDGT: i16 = 25;
pub const MSC_TRICKCASTING: i16 = 26;

/// mob.hpp enum e_random_monster.
pub const MOBG_BRANCH_OF_DEAD_TREE: i32 = 0;
pub const MOBG_PORING_BOX: i32 = 1;
pub const MOBG_BLOODY_DEAD_BRANCH: i32 = 2;
pub const MOBG_RED_POUCH_OF_SURPRISE: i32 = 3;
pub const MOBG_CLASSCHANGE: i32 = 4;
pub const MOBG_TAEKWON_MISSION: i32 = 5;
pub const MOBG_MAX: i32 = 6;

/// mob.hpp enum e_random_monster_flags.
pub const RMF_NONE: u32 = 0x00;
pub const RMF_DB_RATE: u32 = 0x01;
pub const RMF_CHECK_MOB_LV: u32 = 0x02;
pub const RMF_MOB_NOT_BOSS: u32 = 0x04;
pub const RMF_MOB_NOT_SPAWN: u32 = 0x08;
pub const RMF_MOB_NOT_PLANT: u32 = 0x10;
pub const RMF_ALL: u32 = 0xFF;

/// map.hpp enum _look (indexes of view_data::look).
pub const LOOK_BASE: usize = 0;
pub const LOOK_HAIR: usize = 1;
pub const LOOK_WEAPON: usize = 2;
pub const LOOK_HEAD_BOTTOM: usize = 3;
pub const LOOK_HEAD_TOP: usize = 4;
pub const LOOK_HEAD_MID: usize = 5;
pub const LOOK_HAIR_COLOR: usize = 6;
pub const LOOK_CLOTHES_COLOR: usize = 7;
pub const LOOK_SHIELD: usize = 8;
pub const LOOK_SHOES: usize = 9;
pub const LOOK_BODY: usize = 10;
pub const LOOK_RESET_COSTUMES: usize = 11;
pub const LOOK_ROBE: usize = 12;
pub const LOOK_BODY2: usize = 13;
pub const LOOK_MAX: usize = 14;

// ------------------------------------------------------------------------------------------- shared status structs

/// status.hpp struct weapon_atk (RENEWAL layout).
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct WeaponAtk {
    /// For monsters after status_calc_misc: 80% of the db `Attack` (status_base_atk_min).
    pub atk: u16,
    /// For monsters after status_calc_misc: 120% of the db `Attack` (status_base_atk_max). Mercenaries: `Attack` + `Attack2`.
    pub atk2: u16,
    /// `AttackRange`.
    pub range: u16,
    pub ele: u8,
    /// RENEWAL: the db `Attack2` of monsters and elementals.
    pub matk: u16,
    pub wlv: u8,
}

/// status.hpp struct status_data (RENEWAL, RENEWAL_ASPD layout); zero unless a loader sets it.
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct StatusData {
    /// = max_hp once loadingFinished ran.
    pub hp: u32,
    pub sp: u32,
    pub ap: u32,
    /// `Hp` (default 1), times battle_config mvp_hp_rate / monster_hp_rate for monsters.
    pub max_hp: u32,
    /// `Sp` (default 1).
    pub max_sp: u32,
    pub max_ap: u32,
    /// `Str` (default 1 for monsters).
    pub str: i16,
    /// `Agi` (default 1 for monsters).
    pub agi: i16,
    /// `Vit` (default 1 for monsters).
    pub vit: i16,
    /// `Int` (default 1 for monsters).
    pub int_: i16,
    /// `Dex` (default 1 for monsters).
    pub dex: i16,
    /// `Luk` (default 1 for monsters).
    pub luk: i16,
    pub pow: i16,
    pub sta: i16,
    pub wis: i16,
    pub spl: i16,
    pub con: i16,
    pub crt: i16,
    pub eatk: i16,
    /// status_base_atk (monsters: Str + level when battle_config.enable_baseatk_renewal has BL_MOB).
    pub batk: i32,
    pub watk: u16,
    pub watk2: u16,
    /// status_base_matk_min / _max (monsters: Int + level + 70% / 130% of `Attack2`).
    pub matk_min: u16,
    pub matk_max: u16,
    /// `WalkSpeed` (default DEFAULT_WALK_SPEED 150).
    pub speed: u16,
    /// `AttackMotion` (default MAX_ASPD_NOPC 100).
    pub amotion: u16,
    /// `ClientAttackMotion` (default: amotion).
    pub clientamotion: u16,
    /// `AttackDelay` (default MAX_ASPD_NOPC), at least amotion after loadingFinished.
    pub adelay: u16,
    /// `DamageMotion` (times monster_damage_delay_rate for monsters).
    pub dmotion: u16,
    /// e_mode bits: `Ai` (MONSTER_TYPE_xx), `Modes`, and the class modes loadingFinished adds.
    pub mode: i32,
    pub hit: i16,
    pub flee: i16,
    pub cri: i16,
    pub flee2: i16,
    pub def2: i16,
    pub mdef2: i16,
    pub aspd_rate2: i16,
    /// 1000 for database units.
    pub aspd_rate: i16,
    pub patk: i16,
    pub smatk: i16,
    /// `Resistance`.
    pub res: i16,
    /// `MagicResistance`.
    pub mres: i16,
    pub hplus: i16,
    #[serde(rename = "crate")]
    pub crate_: i16,
    /// `Defense` (defType is int16 in RENEWAL).
    pub def: i16,
    /// `MagicDefense`.
    pub mdef: i16,
    /// `Element` (e_element).
    pub def_ele: u8,
    /// `ElementLevel` (default 1).
    pub ele_lv: u8,
    /// `Size` (e_size).
    pub size: u8,
    /// `Race` (e_race).
    pub race: u8,
    /// `Class` (e_aegis_monsterclass).
    pub class_: u8,
    pub rhw: WeaponAtk,
    pub lhw: WeaponAtk,
}

impl StatusData {
    /// status.hpp status_has_mode: every bit of `md` is set.
    pub fn has_mode(&self, md: i32) -> bool {
        self.mode & md == md
    }
}

/// unit.hpp struct view_data.
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct ViewData {
    /// look[LOOK_MAX]; monsters: look[LOOK_BASE] = id (mob_avail.yml `Sprite` can change it).
    pub look: [i32; LOOK_MAX],
    pub sex: i8,
    pub dead_sit: u8,
}

// ------------------------------------------------------------------------------------------------------- mob_db

/// mob.hpp struct s_mob_drop (a `Drops` / `MvpDrops` entry, rate after mob_drop_ratio_adjust).
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct MobDrop {
    /// `Item` (AegisName resolved through item_db.search_aegisname).
    pub nameid: u32,
    /// `Rate` (1..10000, x/10000) after mob_drop_adjust with the battle_config item_rate_* / item_drop_*_min/max.
    pub rate: u32,
    /// `RandomOptionGroup` (option_get_id; 0 none).
    pub randomopt_group: u16,
    /// `StealProtected` (default false; map drops default true).
    pub steal_protected: bool,
}

/// mob.hpp struct s_mob_skill (a db/re/mob_skill_db.txt row after mob_parse_row_mobskilldb). The dataset stores each
/// as an array in field order.
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
pub struct MobSkill {
    /// Column 3 (MobSkillState: any/idle/walk/loot/dead/attack/angry/chase/follow/anytarget).
    pub state: i32,
    /// Column 4.
    pub skill_id: u16,
    /// Column 5 (<= 0 reads as 1, capped to battle_config mob_max_skilllvl).
    pub skill_lv: u16,
    /// Column 6 (x/10000, times mob_skill_rate; 0 becomes 1 while mob_skill_rate is not 0).
    pub permillage: i16,
    /// Column 7.
    pub casttime: i32,
    /// Column 8, times mob_skill_delay; negative or over MOB_MAX_DELAY becomes MOB_MAX_DELAY.
    pub delay: i32,
    /// Column 9 ("yes" is 1).
    pub cancel: i16,
    /// Column 11 (e_mob_skill_condition; -1 when unrecognized).
    pub cond1: i16,
    /// Column 12 (number, or anybad -1 / an SC_ constant).
    pub cond2: i64,
    /// Column 10 (e_mob_skill_target).
    pub target: i16,
    /// Columns 13-17 (strtol base 0); NPC_EMOTION / NPC_EMOTION_ON rewrite them as the loader does.
    pub val: [i32; 5],
    /// Column 18 (-1 when empty).
    pub emotion: i16,
    /// Column 19 (a mob_chat_db id, else 0).
    pub msg_id: u16,
}

/// mob.hpp struct s_mob_db.
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct MobDb {
    /// `Id`.
    pub id: u32,
    /// `AegisName` (std::string resized to NAME_LENGTH).
    pub sprite: String,
    /// `Name`.
    pub name: String,
    /// `JapaneseName` (default: Name).
    pub jname: String,
    /// `BaseExp` times battle_config base_exp_rate / 100 (capped to MAX_EXP).
    pub base_exp: u64,
    /// `JobExp` times job_exp_rate / 100.
    pub job_exp: u64,
    /// `MvpExp` times mvp_exp_rate / 100.
    pub mexp: u64,
    /// `BaseExp` / `JobExp` / `MvpExp` as the node gave them, before the rates: a mob_reload under other rates than
    /// the dataset's ([`MobDatabase::loaded_with`]) multiplies these again (mob/rtdb.rs).
    pub base_exp_raw: u64,
    pub job_exp_raw: u64,
    pub mexp_raw: u64,
    /// `Drops` / `MvpDrops` as the files gave them, before mob_drop_ratio_adjust, for a mob_reload under other
    /// rates. Empty when the adjustment changed nothing: the list is then `dropitem` / `mvpitem` itself.
    pub dropitem_raw: Vec<MobDrop>,
    pub mvpitem_raw: Vec<MobDrop>,
    /// `SkillRange` (times view_range_rate).
    pub range2: u16,
    /// `ChaseRange` (times chase_range_rate).
    pub range3: u16,
    /// `RaceGroups` (e_race2, in the order they were enabled).
    pub race2: Vec<u8>,
    /// `Level` (default 1).
    pub lv: u16,
    /// `Drops` (at most MAX_MOB_DROP; zero-rate entries removed).
    pub dropitem: Vec<MobDrop>,
    /// `MvpDrops` (at most MAX_MVP_DROP).
    pub mvpitem: Vec<MobDrop>,
    pub status: StatusData,
    pub vd: ViewData,
    /// mob_avail.yml `Options` (OPTION_ bits).
    pub option: u32,
    /// mob_skill_db.txt rows (at most MAX_MOBSKILL).
    pub skill: Vec<MobSkill>,
    /// `DamageTaken` (1..100, default 100).
    pub damagetaken: u16,
    /// `GroupId`.
    pub group_id: i32,
    /// `Title`.
    pub title: String,
}

impl MobDb {
    /// s_mob_db::get_bosstype.
    pub fn get_bosstype(&self) -> BossType {
        if self.status.has_mode(MD_MVP) {
            BossType::Mvp
        } else if self.status.class_ == CLASS_BOSS {
            BossType::MiniBoss
        } else {
            BossType::None
        }
    }
}

/// mob.hpp s_mob_item_drop_ratio (db/mob_item_ratio.yml): the drop ratio of one item, for every monster (`mob_ids`
/// empty) or for the listed ones.
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct ItemDropRatio {
    pub nameid: u32,
    pub drop_ratio: u32,
    pub mob_ids: Vec<u16>,
}

#[derive(Deserialize)]
struct MobDbFile {
    mobs: Vec<MobDb>,
    /// The battle_config settings mob_db consumed when datagen loaded it (battle_data name, value).
    loaded_with: Vec<(String, i32)>,
    /// mob_item_ratio.yml as mob_drop_ratio_adjust used it.
    #[serde(default)]
    item_ratio: Vec<ItemDropRatio>,
}

/// MobDatabase (TypesafeCachedYamlDatabase<uint32, s_mob_db>): iteration follows rAthena's unordered_map order,
/// `cache()` its id-ordered cache (getCache).
#[derive(Deserialize)]
#[serde(from = "MobDbFile")]
pub struct MobDatabase {
    list: Vec<MobDb>,
    index: HashMap<u32, usize>,
    cache: Vec<usize>,
    loaded_with: crate::battle_config::MobDbConf,
    item_ratio: HashMap<u32, ItemDropRatio>,
}

impl From<MobDbFile> for MobDatabase {
    fn from(f: MobDbFile) -> Self {
        let index: HashMap<u32, usize> = f.mobs.iter().enumerate().map(|(i, m)| (m.id, i)).collect();
        let mut cache: Vec<usize> = (0..f.mobs.len()).collect();
        cache.sort_by_key(|&i| f.mobs[i].id);
        let mut loaded_with = [0i32; crate::battle_config::MOBDB_N];
        for (j, k) in crate::battle_config::MOBDB_MEMBERS.iter().enumerate() {
            let name = crate::battle_config::BATTLE_DATA[k.0 as usize].name;
            loaded_with[j] = f.loaded_with.iter().find(|(n, _)| n == name).map(|(_, v)| *v).unwrap_or_else(|| panic!("mobs_db: no loaded_with value for {name} (run datagen)"));
        }
        let item_ratio = f.item_ratio.into_iter().map(|r| (r.nameid, r)).collect();
        MobDatabase { list: f.mobs, index, cache, loaded_with, item_ratio }
    }
}

impl MobDatabase {
    /// The settings every entry's EXP and drop lists were made with (battle_config MOBDB_MEMBERS, in that order).
    pub fn loaded_with(&self) -> &crate::battle_config::MobDbConf {
        &self.loaded_with
    }
    /// mob_item_drop_ratio.find(nameid).
    pub fn item_ratio(&self, nameid: u32) -> Option<&ItemDropRatio> {
        self.item_ratio.get(&nameid)
    }
    pub fn find(&self, id: u32) -> Option<&MobDb> {
        self.index.get(&id).map(|&i| &self.list[i])
    }
    pub fn exists(&self, id: u32) -> bool {
        self.index.contains_key(&id)
    }
    pub fn len(&self) -> usize {
        self.list.len()
    }
    pub fn is_empty(&self) -> bool {
        self.list.is_empty()
    }
    /// `for (auto& pair : mob_db)`: rAthena's iteration order.
    pub fn iter(&self) -> impl Iterator<Item = &MobDb> {
        self.list.iter()
    }
    /// mob_db.getCache(): ascending ids.
    pub fn cache(&self) -> impl Iterator<Item = &MobDb> {
        self.cache.iter().map(|&i| &self.list[i])
    }
}

crate::dataset!(mob_db, "mobs_db", MobDatabase);

/// mob.cpp mob_is_clone: the id when it is in the clone range and defined. Clones are made at run time
/// (mob_clone_spawn), so the static database never holds one.
pub fn mob_is_clone(mob_id: i32) -> i32 {
    if !(MOB_CLONE_START..=MOB_CLONE_END).contains(&mob_id) {
        return 0;
    }
    if mob_id < 0 || !mob_db().exists(mob_id as u32) {
        return 0;
    }
    mob_id
}

/// mob.cpp mobdb_checkid: the id when it is a defined, non-clone monster, else 0.
pub fn mobdb_checkid(id: i32) -> i32 {
    if id < 0 || !mob_db().exists(id as u32) {
        return 0;
    }
    if mob_is_clone(id) != 0 {
        return 0;
    }
    id
}

/// mob.cpp mob_get_viewdata.
pub fn mob_get_viewdata(mob_id: i32) -> Option<&'static ViewData> {
    if mob_id < 0 {
        return None;
    }
    mob_db().find(mob_id as u32).map(|m| &m.vd)
}

/// strlib strcmpi (ASCII case folding, bytes above 127 compared as they are).
fn strcmpi_eq(a: &str, b: &str) -> bool {
    a.eq_ignore_ascii_case(b)
}

/// strlib stristr: `needle` occurs in `haystack` ignoring ASCII case (an empty needle always matches).
fn stristr(haystack: &str, needle: &str) -> bool {
    if needle.is_empty() {
        return true;
    }
    let h: Vec<char> = haystack.chars().map(|c| c.to_ascii_lowercase()).collect();
    let n: Vec<char> = needle.chars().map(|c| c.to_ascii_lowercase()).collect();
    h.windows(n.len()).any(|w| w == n.as_slice())
}

/// mob.cpp mobdb_search_aegisname: strcmpi against every AegisName, first match in iteration order.
pub fn mobdb_search_aegisname(name: &str) -> Option<&'static MobDb> {
    mob_db().iter().find(|m| strcmpi_eq(name, &m.sprite))
}

/// mob.cpp mobdb_searchname_sub. `mob_has_spawn` is mob.cpp mob_has_spawn (the spawn data the world build recorded).
pub fn mobdb_searchname_sub(mob_id: u16, name: &str, full_cmp: bool, mob_has_spawn: &dyn Fn(u16) -> bool) -> bool {
    let Some(mob) = mob_db().find(mob_id as u32) else { return false };
    if mobdb_checkid(mob_id as i32) <= 0 {
        return false;
    }
    if strcmpi_eq(&mob.sprite, name) {
        return true;
    }
    if mob.base_exp == 0 && mob.job_exp == 0 && !mob_has_spawn(mob_id) {
        return false; // "slave mobs" are excluded from name searches
    }
    if full_cmp {
        strcmpi_eq(&mob.name, name) || strcmpi_eq(&mob.jname, name)
    } else {
        stristr(&mob.name, name) || stristr(&mob.jname, name) || stristr(&mob.sprite, name)
    }
}

/// mob.cpp mobdb_searchname (full comparison, first match in iteration order; 0 when none).
pub fn mobdb_searchname(name: &str, mob_has_spawn: &dyn Fn(u16) -> bool) -> u16 {
    mob_db().iter().map(|m| m.id as u16).find(|&id| mobdb_searchname_sub(id, name, true, mob_has_spawn)).unwrap_or(0)
}

/// mob.cpp mobdb_searchname_array: up to `size` ids, full matches first, then partial ones (id order).
pub fn mobdb_searchname_array(name: &str, size: usize, mob_has_spawn: &dyn Fn(u16) -> bool) -> Vec<u16> {
    let mut out = Vec::new();
    for m in mob_db().cache() {
        if mobdb_searchname_sub(m.id as u16, name, true, mob_has_spawn) {
            out.push(m.id as u16);
            if out.len() >= size {
                return out;
            }
        }
    }
    for m in mob_db().cache() {
        if mobdb_searchname_sub(m.id as u16, name, false, mob_has_spawn) && !mobdb_searchname_sub(m.id as u16, name, true, mob_has_spawn) {
            out.push(m.id as u16);
            if out.len() >= size {
                return out;
            }
        }
    }
    out
}

// --------------------------------------------------------------------------------------------------- mob_chat_db

/// mob.hpp struct s_mob_chat (db/mob_chat_db.yml).
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct MobChat {
    /// `Id`.
    pub msg_id: u16,
    /// `Color` (strtoul base 0, default 0xFF0000).
    pub color: u32,
    /// `Dialog` (at most CHAT_SIZE_MAX - 1 bytes).
    pub msg: String,
}

#[derive(Deserialize)]
struct MobChatFile {
    chats: Vec<MobChat>,
}

/// MobChatDatabase (uint16 -> s_mob_chat).
#[derive(Deserialize)]
#[serde(from = "MobChatFile")]
pub struct MobChatDatabase {
    by_id: HashMap<u16, MobChat>,
}

impl From<MobChatFile> for MobChatDatabase {
    fn from(f: MobChatFile) -> Self {
        MobChatDatabase { by_id: f.chats.into_iter().map(|c| (c.msg_id, c)).collect() }
    }
}

impl MobChatDatabase {
    pub fn find(&self, id: u16) -> Option<&MobChat> {
        self.by_id.get(&id)
    }
    pub fn len(&self) -> usize {
        self.by_id.len()
    }
    pub fn is_empty(&self) -> bool {
        self.by_id.is_empty()
    }
}

crate::dataset!(mob_chat_db, "mobs_chat", MobChatDatabase);

// ---------------------------------------------------------------------------------------------------- mob_summon

/// mob.hpp struct s_randomsummon_entry.
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct RandomSummonEntry {
    /// `Summon[].Mob`.
    pub mob_id: u16,
    /// `Summon[].Rate` (x/1000000).
    pub rate: u32,
}

/// mob.hpp struct s_randomsummon_group (db/re/mob_summon.yml).
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct RandomSummonGroup {
    /// `Group` (MOBG_ constant).
    pub random_id: u16,
    /// `Default`.
    pub default_mob_id: u16,
    /// `Summon` (an unordered_map in rAthena: this is its iteration order, which umap_random indexes).
    pub list: Vec<RandomSummonEntry>,
}

#[derive(Deserialize)]
struct MobSummonFile {
    groups: Vec<RandomSummonGroup>,
}

/// MobSummonDatabase (uint16 -> s_randomsummon_group).
#[derive(Deserialize)]
#[serde(from = "MobSummonFile")]
pub struct MobSummonDatabase {
    groups: Vec<RandomSummonGroup>,
}

impl From<MobSummonFile> for MobSummonDatabase {
    fn from(f: MobSummonFile) -> Self {
        MobSummonDatabase { groups: f.groups }
    }
}

impl MobSummonDatabase {
    pub fn find(&self, random_id: i32) -> Option<&RandomSummonGroup> {
        self.groups.iter().find(|g| g.random_id as i32 == random_id)
    }
    pub fn len(&self) -> usize {
        self.groups.len()
    }
    pub fn is_empty(&self) -> bool {
        self.groups.is_empty()
    }
}

crate::dataset!(mob_summon_db, "mobs_summon", MobSummonDatabase);

/// mob.cpp mob_get_random_id. `rnd_value(min, max)` / `rnd()` are the reducer RNG (Ctx::rnd_value / Ctx::rnd),
/// `mob_has_spawn` the spawn data. Returns 0 on the errors rAthena reports.
pub fn mob_get_random_id(
    type_: i32,
    flag: u32,
    lv: i32,
    rnd_value: &mut dyn FnMut(i32, i32) -> i32,
    rnd: &mut dyn FnMut() -> u32,
    mob_has_spawn: &dyn Fn(u16) -> bool,
) -> i32 {
    let mut flag = flag;
    let Some(summon) = mob_summon_db().find(type_) else {
        log::error!("mob_get_random_id: Invalid type ({type_}) of random monster.");
        return 0;
    };
    if type_ == MOBG_BLOODY_DEAD_BRANCH && flag & RMF_MOB_NOT_BOSS != 0 {
        flag &= !RMF_MOB_NOT_BOSS;
    }
    if summon.list.is_empty() {
        log::error!("mob_get_random_id: Random monster type {type_} is not defined.");
        return 0;
    }
    for _ in 0..summon.list.len() * 3 {
        let entry = &summon.list[rnd_value(0, summon.list.len() as i32 - 1) as usize];
        let Some(mob) = mob_db().find(entry.mob_id as u32) else { continue };
        if mob_is_clone(entry.mob_id as i32) != 0
            || (flag & RMF_DB_RATE != 0 && entry.rate < 1_000_000 && entry.rate <= rnd() % 1_000_000)
            || (flag & RMF_CHECK_MOB_LV != 0 && lv < mob.lv as i32)
            || (flag & RMF_MOB_NOT_BOSS != 0 && mob.status.has_mode(MD_STATUSIMMUNE))
            || (flag & RMF_MOB_NOT_SPAWN != 0 && !mob_has_spawn(entry.mob_id))
            || (flag & RMF_MOB_NOT_PLANT != 0 && mob.status.has_mode(MD_IGNOREMELEE | MD_IGNOREMAGIC | MD_IGNORERANGED | MD_IGNOREMISC))
        {
            continue;
        }
        return entry.mob_id as i32;
    }
    if !mob_db().exists(summon.default_mob_id as u32) {
        log::error!("mob_get_random_id: Default monster is not defined for type {type_}.");
        return 0;
    }
    summon.default_mob_id as i32
}

// ----------------------------------------------------------------------------------------------------- map_drops

/// mob.hpp struct s_map_drops (db/re/map_drops.yml). A drop is a MobDrop with a rate in 1/100000.
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct MapDrops {
    /// The map id (map_mapindex2mapid; the same numbering as data::maps()).
    pub mapid: i16,
    /// `Map`.
    pub map: String,
    /// `GlobalDrops`: (Index, drop), in rAthena's unordered_map iteration order.
    pub globals: Vec<(u16, MobDrop)>,
    /// `SpecificDrops`: (monster id, [(Index, drop)]), both levels in iteration order.
    pub specific: Vec<(u16, Vec<(u16, MobDrop)>)>,
}

#[derive(Deserialize)]
struct MapDropFile {
    maps: Vec<MapDrops>,
}

/// MapDropDatabase (map id -> s_map_drops).
#[derive(Deserialize)]
#[serde(from = "MapDropFile")]
pub struct MapDropDatabase {
    maps: Vec<MapDrops>,
}

impl From<MapDropFile> for MapDropDatabase {
    fn from(f: MapDropFile) -> Self {
        MapDropDatabase { maps: f.maps }
    }
}

impl MapDropDatabase {
    pub fn find(&self, mapid: i16) -> Option<&MapDrops> {
        self.maps.iter().find(|m| m.mapid == mapid)
    }
    pub fn find_by_name(&self, map: &str) -> Option<&MapDrops> {
        self.maps.iter().find(|m| m.map == map)
    }
    pub fn len(&self) -> usize {
        self.maps.len()
    }
    pub fn is_empty(&self) -> bool {
        self.maps.is_empty()
    }
}

impl MapDrops {
    /// The drops for one monster (`specific[mob_id]`).
    pub fn specific_for(&self, mob_id: u16) -> Option<&[(u16, MobDrop)]> {
        self.specific.iter().find(|(id, _)| *id == mob_id).map(|(_, d)| d.as_slice())
    }
}

crate::dataset!(map_drop_db, "mobs_map_drops", MapDropDatabase);

// ------------------------------------------------------------------------------------------ item_db side effects

/// What mob_drop_ratio_adjust stores into item_data (itemdb.hpp maxchance and mob[MAX_SEARCH]) for one item.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct ItemDropInfo {
    /// item_data::maxchance after the monster drops (before npc shops set -1 on unsold-by-monster items).
    pub maxchance: i32,
    /// item_data::mob[]: (chance, mob id), highest first, only the filled slots.
    pub mob: Vec<(i32, i32)>,
}

#[derive(Deserialize)]
struct ItemDropsFile {
    items: Vec<(u32, i32, Vec<(i32, i32)>)>,
}

#[derive(Deserialize)]
#[serde(from = "ItemDropsFile")]
pub struct ItemDropInfos {
    by_item: HashMap<u32, ItemDropInfo>,
}

impl From<ItemDropsFile> for ItemDropInfos {
    fn from(f: ItemDropsFile) -> Self {
        ItemDropInfos { by_item: f.items.into_iter().map(|(id, maxchance, mob)| (id, ItemDropInfo { maxchance, mob })).collect() }
    }
}

impl ItemDropInfos {
    pub fn find(&self, nameid: u32) -> Option<&ItemDropInfo> {
        self.by_item.get(&nameid)
    }
    pub fn len(&self) -> usize {
        self.by_item.len()
    }
    pub fn is_empty(&self) -> bool {
        self.by_item.is_empty()
    }
}

crate::dataset!(mob_item_drop_info, "mobs_item_drops", ItemDropInfos);

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn counts() {
        assert_eq!(mob_db().len(), 2675);
        assert_eq!(mob_chat_db().len(), 57);
        assert_eq!(mob_summon_db().len(), 6);
        assert_eq!(map_drop_db().len(), 18);
        // every mob_skill_db.txt row landed on its monster
        assert_eq!(mob_db().iter().map(|m| m.skill.len()).sum::<usize>(), 14514);
    }

    #[test]
    fn poring() {
        let m = mob_db().find(1002).expect("PORING");
        assert_eq!((m.sprite.as_str(), m.name.as_str(), m.jname.as_str(), m.lv), ("PORING", "Poring", "Poring", 1));
        // BaseExp 150 / JobExp 40 at the Loki rates (base_exp_rate / job_exp_rate 500)
        assert_eq!((m.base_exp, m.job_exp, m.mexp), (750, 200, 0));
        let s = &m.status;
        assert_eq!((s.max_hp, s.hp, s.max_sp), (55, 55, 1));
        assert_eq!((s.str, s.agi, s.vit, s.int_, s.dex, s.luk), (6, 1, 1, 0, 6, 5));
        // status_calc_misc: hit = lv + dex + 150, flee = lv + agi + 100, def2 = (lv + vit) / 2, batk = str + lv
        assert_eq!((s.hit, s.flee, s.def2, s.mdef2, s.batk, s.cri, s.flee2), (157, 102, 1, 0, 7, 0, 0));
        assert_eq!((s.rhw.atk, s.rhw.atk2, s.rhw.matk, s.matk_min, s.matk_max, s.rhw.range), (0, 1, 1, 1, 2, 1));
        assert_eq!((s.def, s.mdef, s.speed, s.adelay, s.amotion, s.clientamotion, s.dmotion), (2, 5, 400, 1872, 672, 288, 480));
        assert_eq!((s.size, s.race, s.def_ele, s.ele_lv, s.class_, s.aspd_rate), (1, 3, 1, 1, CLASS_NORMAL, 1000));
        assert_eq!(s.mode, 0x83); // Ai 02
        assert_eq!((m.range2, m.range3, m.damagetaken), (10, 12, 100));
        assert_eq!(m.vd.look[LOOK_BASE], 1002);
        assert_eq!(m.get_bosstype(), BossType::None);
        // drop rates at item_rate_* 100 (1..10000): Jellopy 70%, Knife_ 1%, Sticky_Mucus 4%, Apple 10%, Wing_Of_Fly
        // 5%, Apple 1.5%, Unripe_Apple 0.2%, Poring_Card 0.2% (steal protected)
        let d: Vec<(u32, u32, bool)> = m.dropitem.iter().map(|d| (d.nameid, d.rate, d.steal_protected)).collect();
        assert_eq!(
            d,
            vec![(909, 7000, false), (1202, 100, false), (938, 400, false), (512, 1000, false), (601, 500, false), (512, 150, false), (619, 20, false), (4001, 20, true)]
        );
        assert!(m.mvpitem.is_empty());
        let sk: Vec<(i32, u16, u16, i16, i16)> = m.skill.iter().map(|s| (s.state, s.skill_id, s.skill_lv, s.permillage, s.target)).collect();
        assert_eq!(sk, vec![(MSS_LOOT, 197, 1, 2000, MST_SELF), (MSS_BERSERK, 184, 1, 2000, MST_TARGET)]);
        assert_eq!(m.skill[0].val, [2, 0, 0, 0, 0]); // NPC_EMOTION with emotion 2
        assert_eq!((m.skill[0].delay, m.skill[0].cancel, m.skill[0].emotion), (5000, 1, -1));
    }

    #[test]
    fn baphomet() {
        let m = mob_db().find(1039).expect("BAPHOMET");
        assert_eq!(m.get_bosstype(), BossType::Mvp);
        assert_eq!((m.lv, m.base_exp, m.job_exp, m.mexp), (81, 1_090_445, 835_265, 109_044));
        let s = &m.status;
        assert_eq!(s.max_hp, 668_000);
        // CLASS_BOSS adds MD_DETECTOR | MD_STATUSIMMUNE | MD_KNOCKBACKIMMUNE in loadingFinished
        assert!(s.has_mode(MD_MVP | MD_DETECTOR | MD_STATUSIMMUNE | MD_KNOCKBACKIMMUNE));
        assert_eq!(s.class_, CLASS_BOSS);
        assert_eq!((s.hit, s.flee, s.def2, s.mdef2, s.batk), (417, 306, 55, 41, 201));
        assert_eq!((s.rhw.atk, s.rhw.atk2, s.matk_min, s.matk_max), (2520, 3780, 1554, 2745));
        assert_eq!(m.damagetaken, 10);
        let mvp: Vec<(u32, u32)> = m.mvpitem.iter().map(|d| (d.nameid, d.rate)).collect();
        assert_eq!(mvp, vec![(23817, 5000), (750, 2500), (923, 5000)]);
        assert_eq!(m.dropitem.len(), 8);
        assert_eq!(m.skill.len(), 23);
    }

    #[test]
    fn lookups() {
        assert_eq!(mobdb_search_aegisname("poring").map(|m| m.id), Some(1002));
        assert_eq!(mobdb_search_aegisname("BaPhOmEt").map(|m| m.id), Some(1039));
        assert!(mobdb_search_aegisname("NO_SUCH_MOB").is_none());
        assert_eq!(mobdb_checkid(1002), 1002);
        assert_eq!(mobdb_checkid(1003), 0); // commented out in mob_db.yml
        assert_eq!(mobdb_checkid(-1), 0);
        assert_eq!(mob_is_clone(1002), 0);
        assert_eq!(mob_is_clone(4000), 0);
        assert_eq!(mob_get_viewdata(1002).map(|v| v.look[LOOK_BASE]), Some(1002));
        let no_spawn = |_: u16| false;
        // six monsters are named Baphomet; rAthena returns the first with exp in its unordered_map order
        assert_eq!(mobdb_searchname("Baphomet", &no_spawn), 2100);
        // full matches first (id order), then the partial ones (Baphomet Jr., ...)
        // (monsters without exp count only when they have a spawn)
        assert_eq!(mobdb_searchname_array("Baphomet", 10, &no_spawn), vec![1039, 1399, 1848, 2100, 1101, 1929, 2483]);
        assert_eq!(mobdb_searchname_array("Baphomet", 10, &|id| id == 1897)[..5], [1039, 1399, 1848, 1897, 2100]);
        // an exact AegisName always matches, but so does any earlier monster whose Name is "Poring" (1973 comes first)
        assert_eq!(mobdb_searchname("PORING", &no_spawn), 1973);
        assert!(mobdb_searchname_sub(1002, "PORING", true, &no_spawn));
        // the cache is ordered by id
        let ids: Vec<u32> = mob_db().cache().map(|m| m.id).collect();
        assert!(ids.windows(2).all(|w| w[0] < w[1]));
    }

    #[test]
    fn chat_summon_drops() {
        let c = mob_chat_db().find(1).expect("chat 1");
        assert_eq!((c.color, c.msg.as_str()), (0xFF0000, "Weakling! Challenge me if you have any courage!"));
        let g = mob_summon_db().find(MOBG_BRANCH_OF_DEAD_TREE).expect("branch");
        assert!(!g.list.is_empty());
        assert!(mob_db().exists(g.default_mob_id as u32));
        let mut rv = |a: i32, _b: i32| a; // umap_random always takes the first entry
        let mut r = || 0u32;
        let id = mob_get_random_id(MOBG_BRANCH_OF_DEAD_TREE, RMF_NONE, 99, &mut rv, &mut r, &|_| true);
        assert_eq!(id, g.list[0].mob_id as i32);
        assert_eq!(mob_get_random_id(MOBG_MAX, RMF_NONE, 99, &mut rv, &mut r, &|_| true), 0);
        for md in map_drop_db().maps.iter() {
            assert!(crate::data::maps().id_of(&md.map) == Some(md.mapid as u16), "{}", md.map);
            for (_, d) in md.globals.iter() {
                assert!(d.rate >= 1 && d.rate <= 100_000);
            }
        }
        // Poring card: dropped by Poring (20 = 0.2%) and others; maxchance is the highest common drop rate
        let i = mob_item_drop_info().find(4001).expect("Poring_Card");
        assert!(i.mob.iter().any(|&(c, id)| (c, id) == (20, 1002)));
        assert!(i.mob.windows(2).all(|w| w[0].0 >= w[1].0));
        assert_eq!(i.maxchance, i.mob[0].0);
    }
}
