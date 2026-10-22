//! skill.cpp's databases (rAthena e985006, RENEWAL): SkillDatabase (db/re/skill_db.yml + import), skill_nocast_db.txt,
//! produce_db.txt, skill_changematerial_db.txt, skill_damage_db.txt, AbraDatabase, MagicMushroomDatabase,
//! ReadingSpellbookDatabase, SkillArrowDatabase, skill_init_unit_layout / skill_init_nounit_layout, and guild.cpp's
//! GuildSkillTreeDatabase — loaded by tools/datagen_mods/skills.py with rAthena's load rules (defaults, import
//! overrides, per-level arrays expanded by SkillDatabase::parseNode's trend rule, flags resolved to bits, the index
//! table, battle_config values read at load). Lists that rAthena keeps in an unordered_map are in libstdc++'s
//! iteration order (the order rAthena's loops and `random()` picks see).
//!
//! The accessors are the skill.cpp `skill_get_*` family with the `skill_get` / `skill_get_lv` macro semantics.

pub mod ids;

use std::collections::HashMap;

use serde::Deserialize;

pub use ids::*;

/// skill.hpp MAX_SKILL_LEVEL: the per-level arrays' length.
pub const MAX_SKILL_LEVEL: usize = 13;
/// skill.hpp MAX_SKILL_ITEM_REQUIRE.
pub const MAX_SKILL_ITEM_REQUIRE: usize = 10;
/// skill.hpp MAX_SKILL_PRODUCE_DB / MAX_PRODUCE_RESOURCE.
pub const MAX_SKILL_PRODUCE_DB: usize = 300;
pub const MAX_PRODUCE_RESOURCE: usize = 12;
/// skill.cpp MAX_SKILL_CHANGEMATERIAL_DB / MAX_SKILL_CHANGEMATERIAL_SET.
pub const MAX_SKILL_CHANGEMATERIAL_DB: usize = 75;
pub const MAX_SKILL_CHANGEMATERIAL_SET: usize = 3;
/// mmo.hpp MAX_SKILL (skill list and skill_db limit).
pub const MAX_SKILL: usize = 1641;
/// skill.hpp MAX_SQUARE_LAYOUT / MAX_SKILL_UNIT_LAYOUT / MAX_SKILL_UNIT_LAYOUT2.
pub const MAX_SQUARE_LAYOUT: i32 = 7;
pub const MAX_SKILL_UNIT_LAYOUT: i32 = 48 + MAX_SQUARE_LAYOUT;
pub const MAX_SKILL_UNIT_LAYOUT2: usize = 17;
/// mmo.hpp MAX_GUILDSKILL (RENEWAL).
pub const MAX_GUILDSKILL: u16 = 20;

// skill.hpp e_skill_nk (bit numbers of s_skill_db::nk)
pub const NK_NODAMAGE: u8 = 0;
pub const NK_SPLASH: u8 = 1;
pub const NK_SPLASHSPLIT: u8 = 2;
pub const NK_IGNOREATKCARD: u8 = 3;
pub const NK_IGNOREELEMENT: u8 = 4;
pub const NK_IGNOREDEFENSE: u8 = 5;
pub const NK_IGNOREFLEE: u8 = 6;
pub const NK_IGNOREDEFCARD: u8 = 7;
pub const NK_CRITICAL: u8 = 8;
pub const NK_IGNORELONGCARD: u8 = 9;
pub const NK_SIMPLEDEFENSE: u8 = 10;

// skill.hpp e_skill_inf
pub const INF_PASSIVE_SKILL: u16 = 0x00;
pub const INF_ATTACK_SKILL: u16 = 0x01;
pub const INF_GROUND_SKILL: u16 = 0x02;
pub const INF_SELF_SKILL: u16 = 0x04;
pub const INF_SUPPORT_SKILL: u16 = 0x10;
pub const INF_TRAP_SKILL: u16 = 0x20;

// skill.hpp e_skill_inf2 (bit numbers of s_skill_db::inf2)
pub const INF2_ISQUEST: u8 = 0;
pub const INF2_ISNPC: u8 = 1;
pub const INF2_ISWEDDING: u8 = 2;
pub const INF2_ISSPIRIT: u8 = 3;
pub const INF2_ISGUILD: u8 = 4;
pub const INF2_ISSONG: u8 = 5;
pub const INF2_ISENSEMBLE: u8 = 6;
pub const INF2_ISTRAP: u8 = 7;
pub const INF2_TARGETSELF: u8 = 8;
pub const INF2_NOTARGETSELF: u8 = 9;
pub const INF2_PARTYONLY: u8 = 10;
pub const INF2_GUILDONLY: u8 = 11;
pub const INF2_NOTARGETENEMY: u8 = 12;
pub const INF2_ISAUTOSHADOWSPELL: u8 = 13;
pub const INF2_ISCHORUS: u8 = 14;
pub const INF2_IGNOREBGREDUCTION: u8 = 15;
pub const INF2_IGNOREGVGREDUCTION: u8 = 16;
pub const INF2_DISABLENEARNPC: u8 = 17;
pub const INF2_TARGETTRAP: u8 = 18;
pub const INF2_IGNORELANDPROTECTOR: u8 = 19;
pub const INF2_ALLOWWHENHIDDEN: u8 = 20;
pub const INF2_ALLOWWHENPERFORMING: u8 = 21;
pub const INF2_TARGETEMPERIUM: u8 = 22;
pub const INF2_IGNOREKAGEHUMI: u8 = 23;
pub const INF2_ALTERRANGEVULTURE: u8 = 24;
pub const INF2_ALTERRANGESNAKEEYE: u8 = 25;
pub const INF2_ALTERRANGESHADOWJUMP: u8 = 26;
pub const INF2_ALTERRANGERADIUS: u8 = 27;
pub const INF2_ALTERRANGERESEARCHTRAP: u8 = 28;
pub const INF2_IGNOREHOVERING: u8 = 29;
pub const INF2_ALLOWONWARG: u8 = 30;
pub const INF2_ALLOWONMADO: u8 = 31;
pub const INF2_TARGETMANHOLE: u8 = 32;
pub const INF2_TARGETHIDDEN: u8 = 33;
pub const INF2_INCREASEDANCEWITHWUGDAMAGE: u8 = 34;
pub const INF2_IGNOREWUGBITE: u8 = 35;
pub const INF2_IGNOREAUTOGUARD: u8 = 36;
pub const INF2_IGNORECICADA: u8 = 37;
pub const INF2_SHOWSCALE: u8 = 38;
pub const INF2_IGNOREGTB: u8 = 39;
pub const INF2_TOGGLEABLE: u8 = 40;
pub const INF2_IGNORENONCRITATKBONUS: u8 = 41;
pub const INF2_MAX: u8 = 42;

// skill.hpp e_skill_require
pub const SKILL_REQ_HPCOST: u16 = 0x1;
pub const SKILL_REQ_SPCOST: u16 = 0x2;
pub const SKILL_REQ_HPRATECOST: u16 = 0x4;
pub const SKILL_REQ_SPRATECOST: u16 = 0x8;
pub const SKILL_REQ_MAXHPTRIGGER: u16 = 0x10;
pub const SKILL_REQ_ZENYCOST: u16 = 0x20;
pub const SKILL_REQ_WEAPON: u16 = 0x40;
pub const SKILL_REQ_AMMO: u16 = 0x80;
pub const SKILL_REQ_STATE: u16 = 0x100;
pub const SKILL_REQ_STATUS: u16 = 0x200;
pub const SKILL_REQ_SPIRITSPHERECOST: u16 = 0x400;
pub const SKILL_REQ_ITEMCOST: u16 = 0x800;
pub const SKILL_REQ_EQUIPMENT: u16 = 0x1000;
pub const SKILL_REQ_APCOST: u16 = 0x2000;
pub const SKILL_REQ_APRATECOST: u16 = 0x4000;

// skill.hpp e_skill_nonear_npc
pub const SKILL_NONEAR_WARPPORTAL: u16 = 0x1;
pub const SKILL_NONEAR_SHOP: u16 = 0x2;
pub const SKILL_NONEAR_NPC: u16 = 0x4;
pub const SKILL_NONEAR_TOMB: u16 = 0x8;

// skill.hpp e_skill_cast_flags
pub const SKILL_CAST_IGNOREDEX: u8 = 0x1;
pub const SKILL_CAST_IGNORESTATUS: u8 = 0x2;
pub const SKILL_CAST_IGNOREITEMBONUS: u8 = 0x4;

// skill.hpp e_skill_copyable_option
pub const SKILL_COPY_PLAGIARISM: u8 = 0x1;
pub const SKILL_COPY_REPRODUCE: u8 = 0x2;

// skill.hpp e_skill_unit_flag (bit numbers of s_skill_db::unit_flag)
pub const UF_NONE: u8 = 0;
pub const UF_NOENEMY: u8 = 1;
pub const UF_NOREITERATION: u8 = 2;
pub const UF_NOFOOTSET: u8 = 3;
pub const UF_NOOVERLAP: u8 = 4;
pub const UF_PATHCHECK: u8 = 5;
pub const UF_NOPC: u8 = 6;
pub const UF_NOMOB: u8 = 7;
pub const UF_SKILL: u8 = 8;
pub const UF_DANCE: u8 = 9;
pub const UF_ENSEMBLE: u8 = 10;
pub const UF_SONG: u8 = 11;
pub const UF_DUALMODE: u8 = 12;
pub const UF_NOKNOCKBACK: u8 = 13;
pub const UF_RANGEDSINGLEUNIT: u8 = 14;
pub const UF_CRAZYWEEDIMMUNE: u8 = 15;
pub const UF_REMOVEDBYFIRERAIN: u8 = 16;
pub const UF_KNOCKBACKGROUP: u8 = 17;
pub const UF_HIDDENTRAP: u8 = 18;

// skill.hpp e_require_state
pub const ST_NONE: i32 = 0;
pub const ST_HIDDEN: i32 = 1;
pub const ST_RIDING: i32 = 2;
pub const ST_FALCON: i32 = 3;
pub const ST_CART: i32 = 4;
pub const ST_SHIELD: i32 = 5;
pub const ST_RECOVER_WEIGHT_RATE: i32 = 6;
pub const ST_MOVE_ENABLE: i32 = 7;
pub const ST_WATER: i32 = 8;
pub const ST_RIDINGDRAGON: i32 = 9;
pub const ST_WUG: i32 = 10;
pub const ST_RIDINGWUG: i32 = 11;
pub const ST_MADO: i32 = 12;
pub const ST_ELEMENTALSPIRIT: i32 = 13;
pub const ST_ELEMENTALSPIRIT2: i32 = 14;
pub const ST_PECO: i32 = 15;
pub const ST_SUNSTANCE: i32 = 16;
pub const ST_MOONSTANCE: i32 = 17;
pub const ST_STARSTANCE: i32 = 18;
pub const ST_UNIVERSESTANCE: i32 = 19;

// map.hpp e_skill_damage_type
pub const SKILLDMG_PC: usize = 0;
pub const SKILLDMG_MOB: usize = 1;
pub const SKILLDMG_BOSS: usize = 2;
pub const SKILLDMG_OTHER: usize = 3;
pub const SKILLDMG_MAX: usize = 4;

/// clif.hpp e_damage_type DMG_NORMAL (skill_get_hit's value for an unknown skill).
pub const DMG_NORMAL: u8 = 0;
/// status.hpp SC_NONE.
pub const SC_NONE: i16 = -1;
/// battle.hpp BCT_ALL and map.hpp BL_ALL (skill_get_unit_target / skill_get_unit_bl_target masks).
pub const BCT_ALL: i32 = 0x3F0000;
pub const BL_ALL: i32 = 0xFFF;

/// skill.hpp e_cast_type.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum CastType {
    Ground = 0,
    Damage = 1,
    NoDamage = 2,
}

/// skill.hpp s_skill_require (per skill; the per-level arrays are MAX_SKILL_LEVEL long, filled by parseNode).
#[derive(Deserialize, Clone, Debug)]
pub struct SkillRequire {
    /// Requires.HpCost (Amount per Level; default 0).
    pub hp: [i32; MAX_SKILL_LEVEL],
    /// Requires.MaxHpTrigger (Amount; default 0).
    pub mhp: [i32; MAX_SKILL_LEVEL],
    /// Requires.SpCost (Amount; default 0).
    pub sp: [i32; MAX_SKILL_LEVEL],
    /// Requires.ApCost (Amount; default 0).
    pub ap: [i32; MAX_SKILL_LEVEL],
    /// Requires.HpRateCost (Amount; default 0).
    pub hp_rate: [i32; MAX_SKILL_LEVEL],
    /// Requires.SpRateCost (Amount; default 0).
    pub sp_rate: [i32; MAX_SKILL_LEVEL],
    /// Requires.ApRateCost (Amount; default 0).
    pub ap_rate: [i32; MAX_SKILL_LEVEL],
    /// Requires.ZenyCost (Amount; default 0).
    pub zeny: [i32; MAX_SKILL_LEVEL],
    /// Requires.Weapon: bitmask of 1 << W_* (`All: true` clears it; default 0 = any).
    pub weapon: i32,
    /// Requires.Ammo: bitmask of 1 << AMMO_* (`None: true` clears it; default 0).
    pub ammo: i32,
    /// Requires.AmmoAmount (Amount; needs an Ammo type first; default 0).
    pub ammo_qty: [i32; MAX_SKILL_LEVEL],
    /// Requires.State: ST_* (default ST_NONE).
    pub state: i32,
    /// Requires.SpiritSphereCost (Amount; default 0).
    pub spiritball: [i32; MAX_SKILL_LEVEL],
    /// Requires.ItemCost Item (AEGIS name resolved to the id); slot = list position, or Level - 1 when Level is given.
    pub itemid: [u32; MAX_SKILL_ITEM_REQUIRE],
    /// Requires.ItemCost Amount (same slots as `itemid`).
    pub amount: [i32; MAX_SKILL_ITEM_REQUIRE],
    /// Requires.Equipment: item ids that must be equipped (`eqItem`).
    pub eq_item: Vec<u32>,
    /// Requires.Status: SC_* that must be active.
    pub status: Vec<i16>,
    /// True when any ItemCost entry had a Level (the item slots are per skill level).
    pub itemid_level_dependent: bool,
}

/// map.hpp s_skill_damage (skill_damage_db.txt).
#[derive(Deserialize, Clone, Debug)]
pub struct SkillDamage {
    /// Map types bitmask (column 3; OR-ed).
    pub map: u32,
    /// Caster types bitmask (column 2, number or constant; OR-ed).
    pub caster: u16,
    /// Rates per SKILLDMG_PC / MOB / BOSS / OTHER (columns 4..7, capped to -100..100000).
    pub rate: [i32; SKILLDMG_MAX],
}

/// skill.hpp s_skill_copyable.
#[derive(Deserialize, Clone, Debug)]
pub struct SkillCopyable {
    /// CopyFlags.Skill: SKILL_COPY_PLAGIARISM | SKILL_COPY_REPRODUCE. (A `false` keeps only that bit: rAthena's
    /// `option &= FLAG`.)
    pub option: u8,
    /// CopyFlags.RemoveRequirement: SKILL_REQ_* bits (the true/false value is not read).
    pub req_opt: u16,
}

/// skill.hpp s_skill_db after SkillDatabase::parseBodyNode, skill_nocast_db.txt, skill_damage_db.txt and
/// skill_init_unit_layout (which rewrites unit_layout_type of the special-layout skills).
#[derive(Deserialize, Clone, Debug)]
pub struct SkillDb {
    /// Id.
    pub nameid: u16,
    /// Name (AEGIS name; at most 40 characters).
    pub name: String,
    /// Description (English name; at most 40 characters).
    pub desc: String,
    /// Range (Size per Level; default 0). Negative: melee range that follows the weapon (skill_get_range2).
    pub range: [i32; MAX_SKILL_LEVEL],
    /// Hit: DMG_* (default DMG_NORMAL).
    pub hit: u8,
    /// TargetType: INF_*_SKILL (default INF_PASSIVE_SKILL).
    pub inf: u16,
    /// Element: ELE_* per level (Weapon = ELE_WEAPON, Endowed, Random; default ELE_NEUTRAL).
    pub element: [i8; MAX_SKILL_LEVEL],
    /// DamageFlags: bit NK_* (default none).
    pub nk: u64,
    /// SplashArea (Area per Level; default 0; negative = AREA_SIZE, see skill_get_splash).
    pub splash: [i32; MAX_SKILL_LEVEL],
    /// MaxLevel (1..13).
    pub max: u16,
    /// HitCount (Count per Level; default 0; negative = damage split into that many hits shown).
    pub num: [i32; MAX_SKILL_LEVEL],
    /// CastCancel (default true).
    pub castcancel: bool,
    /// CastDefenseReduction (default 0).
    pub cast_def_rate: u16,
    /// Type: BF_NONE / BF_WEAPON / BF_MAGIC / BF_MISC (default BF_NONE).
    pub skill_type: u16,
    /// Knockback (Amount per Level; default 0).
    pub blewcount: [i32; MAX_SKILL_LEVEL],
    /// Flags: bit INF2_* (default none).
    pub inf2: u64,
    /// ActiveInstance (Max per Level; default 0).
    pub maxcount: [i32; MAX_SKILL_LEVEL],
    /// CastTimeFlags: SKILL_CAST_* (default 0).
    pub castnodex: u8,
    /// CastDelayFlags: SKILL_CAST_* (default 0).
    pub delaynodex: u8,
    /// skill_nocast_db.txt zone flags (OR-ed; default 0).
    pub nocast: u32,
    /// GiveAp (Amount per Level; default 0).
    pub giveap: [i32; MAX_SKILL_LEVEL],
    /// Unit.Id: UNT_* (0 without a Unit).
    pub unit_id: u16,
    /// Unit.AlternateId: UNT_* (default 0).
    pub unit_id2: u16,
    /// Unit.Layout (Size per Level; default 0; -1 = special layout, replaced by its skill_unit_layout index at load).
    pub unit_layout_type: [i32; MAX_SKILL_LEVEL],
    /// Unit.Range (Size per Level; default 0).
    pub unit_range: [i32; MAX_SKILL_LEVEL],
    /// Unit.Interval (default 0).
    pub unit_interval: i16,
    /// Unit.Target: BCT_* | BL_* (default BCT_ALL; BCT_NOENEMY with UF_NOENEMY and defunit_not_enemy; then BL_CHAR
    /// added, BL_PC / BL_MOB removed for UF_NOPC / UF_NOMOB, BL_SKILL added for UF_SKILL). 0 without a Unit.
    pub unit_target: i32,
    /// Unit.Flag: bit UF_* (default none).
    pub unit_flag: u64,
    /// CastTime (Time per Level; default 0) — variable cast time.
    pub cast: [i32; MAX_SKILL_LEVEL],
    /// AfterCastActDelay (Time per Level; default 0).
    pub delay: [i32; MAX_SKILL_LEVEL],
    /// AfterCastWalkDelay (Time per Level; default 0).
    pub walkdelay: [i32; MAX_SKILL_LEVEL],
    /// Duration1 (Time per Level; default 0).
    pub upkeep_time: [i32; MAX_SKILL_LEVEL],
    /// Duration2 (Time per Level; default 0).
    pub upkeep_time2: [i32; MAX_SKILL_LEVEL],
    /// Cooldown (Time per Level; default 0).
    pub cooldown: [i32; MAX_SKILL_LEVEL],
    /// FixedCastTime (Time per Level; default 0; RENEWAL_CAST).
    pub fixed_cast: [i32; MAX_SKILL_LEVEL],
    /// Requires.
    pub require: SkillRequire,
    /// NoNearNPC.AdditionalRange (default 0).
    pub unit_nonearnpc_range: u16,
    /// NoNearNPC.Type: SKILL_NONEAR_* (default 0).
    pub unit_nonearnpc_type: u16,
    /// skill_damage_db.txt.
    pub damage: SkillDamage,
    /// CopyFlags.
    pub copyable: SkillCopyable,
    /// Status: the SC_* the skill starts (default SC_NONE).
    pub sc: i16,
    /// skill_get_index(nameid): position of first registration (SkillDatabase::skilldb_id2idx).
    pub index: u16,
}

impl SkillDb {
    pub fn has_nk(&self, nk: u8) -> bool {
        self.nk >> nk & 1 != 0
    }
    pub fn has_inf2(&self, inf2: u8) -> bool {
        self.inf2 >> inf2 & 1 != 0
    }
    pub fn has_unit_flag(&self, uf: u8) -> bool {
        self.unit_flag >> uf & 1 != 0
    }
}

#[derive(Deserialize)]
struct SkillFile {
    skills: Vec<SkillDb>,
    skill_num: u16,
    loaded: Vec<(String, u32)>,
}

/// skill.cpp SkillDatabase after load: `list` in the unordered_map's iteration order (the order of rAthena's
/// `for (auto& it : skill_db)` loops).
pub struct SkillDatabase {
    pub list: Vec<SkillDb>,
    by_id: HashMap<u16, usize>,
    by_name: HashMap<String, u16>,
    /// SkillDatabase::skill_num (count + 1).
    pub skill_num: u16,
    /// (file, entries) as rAthena's "Done reading" lines report them (skill_db, skill_nocast_db, skill_damage_db).
    pub loaded: Vec<(String, u32)>,
}

impl From<SkillFile> for SkillDatabase {
    fn from(f: SkillFile) -> Self {
        let by_id = f.skills.iter().enumerate().map(|(i, s)| (s.nameid, i)).collect();
        let mut by_name = HashMap::new();
        for s in &f.skills {
            by_name.entry(s.name.to_ascii_lowercase()).or_insert(s.nameid);
        }
        SkillDatabase { list: f.skills, by_id, by_name, skill_num: f.skill_num, loaded: f.loaded }
    }
}

impl<'de> Deserialize<'de> for SkillDatabase {
    fn deserialize<D: serde::Deserializer<'de>>(d: D) -> Result<Self, D::Error> {
        SkillFile::deserialize(d).map(Into::into)
    }
}

impl SkillDatabase {
    pub fn find(&self, skill_id: u16) -> Option<&SkillDb> {
        self.by_id.get(&skill_id).map(|&i| &self.list[i])
    }
    pub fn exists(&self, skill_id: u16) -> bool {
        self.by_id.contains_key(&skill_id)
    }
    pub fn iter(&self) -> impl Iterator<Item = &SkillDb> {
        self.list.iter()
    }
    /// SkillDatabase::get_index (skill_get_index): 0 when the skill is undefined (rAthena logs it unless silent).
    pub fn get_index(&self, skill_id: u16, silent: bool) -> u16 {
        let idx = self.find(skill_id).map(|s| s.index).unwrap_or(0);
        if idx == 0 && skill_id != 0 && !silent {
            log::error!("Skill '{skill_id}' is undefined!");
        }
        idx
    }
}

crate::dataset!(skill_db, "skills_db", SkillDatabase);

/// skill.hpp skill_get_index.
pub fn skill_get_index(skill_id: u16) -> u16 {
    skill_db().get_index(skill_id, false)
}

/// skill.cpp skill_check: 0 and undefined skills fail every accessor.
fn skill_check(id: u16) -> Option<&'static SkillDb> {
    if id == 0 || skill_get_index(id) == 0 {
        return None;
    }
    skill_db().find(id)
}

/// skill.cpp skill_name2id: case-insensitive AEGIS name, the first skill in skill_db iteration order; 0 if none.
pub fn skill_name2id(name: &str) -> u16 {
    skill_db().by_name.get(&name.to_ascii_lowercase()).copied().unwrap_or(0)
}

/// skill.cpp skill_get_name (AEGIS name). rAthena dereferences a missing skill; "" here.
pub fn skill_get_name(skill_id: u16) -> &'static str {
    skill_db().find(skill_id).map(|s| s.name.as_str()).unwrap_or("")
}

/// skill.cpp skill_get_desc (English name). rAthena dereferences a missing skill; "" here.
pub fn skill_get_desc(skill_id: u16) -> &'static str {
    skill_db().find(skill_id).map(|s| s.desc.as_str()).unwrap_or("")
}

/// skill.cpp `skill_get_lv(id, lv, arrvar)`: the level is capped to MAX_SKILL_LEVEL; above it, when the level-13 value is
/// > 1, the last three values are extrapolated. `before` is what rAthena reads for level 0 (`arrvar[-1]`: the 4 bytes
/// in front of the array in s_skill_db on the oracle's x86-64 build — see each accessor).
fn lv_value(arr: &[i32; MAX_SKILL_LEVEL], lv: u16, before: i32) -> i32 {
    let capped = (lv as usize).min(MAX_SKILL_LEVEL);
    if capped == 0 {
        return before;
    }
    let lv_idx = capped - 1;
    if lv as usize > MAX_SKILL_LEVEL && arr[lv_idx] > 1 && lv_idx > 1 {
        let (a, b, c) = (arr[lv_idx - 2], arr[lv_idx - 1], arr[lv_idx]);
        let over = lv as i32 - MAX_SKILL_LEVEL as i32;
        return c
            .wrapping_add((over + 1).wrapping_mul(b.wrapping_sub(a)) / 2)
            .wrapping_add(over.wrapping_mul(c.wrapping_sub(b)) / 2);
    }
    arr[lv_idx]
}

/// Byte `i` of a 40-byte `char[]` field copied from `s` (zero padded).
fn cbyte(s: &str, i: usize) -> u8 {
    crate::net::wire::latin1(s).get(i).copied().unwrap_or(0)
}

// Level 0 (`arrvar[-1]`) of each array, from s_skill_db's layout (offsets checked with offsetof against the headers,
// g++ 13 x86-64): the int32 in front of each array. Padding is zero (make_shared value-initialises).
fn before_range(s: &SkillDb) -> i32 {
    // bytes 80..84: desc[38], desc[39], two padding bytes
    i32::from_le_bytes([cbyte(&s.desc, 38), cbyte(&s.desc, 39), 0, 0])
}

// skill_get family (skill.cpp)

/// skill_get_hit: DMG_NORMAL for an unknown skill.
pub fn skill_get_hit(id: u16) -> u8 {
    skill_check(id).map(|s| s.hit).unwrap_or(DMG_NORMAL)
}
pub fn skill_get_inf(id: u16) -> i32 {
    skill_check(id).map(|s| s.inf as i32).unwrap_or(0)
}
/// skill_get_ele (level 0 reads the high byte of `inf`).
pub fn skill_get_ele(id: u16, lv: u16) -> i32 {
    skill_check(id)
        .map(|s| {
            let arr = s.element.map(|e| e as i32);
            lv_value(&arr, lv, (s.inf >> 8) as u8 as i8 as i32)
        })
        .unwrap_or(0)
}
pub fn skill_get_max(id: u16) -> i32 {
    skill_check(id).map(|s| s.max as i32).unwrap_or(0)
}
/// skill_get_range (level 0 reads desc[38..40]).
pub fn skill_get_range(id: u16, lv: u16) -> i32 {
    skill_check(id).map(|s| lv_value(&s.range, lv, before_range(s))).unwrap_or(0)
}
/// skill_get_splash_ (the raw value; level 0 reads the unused high word of `nk`).
pub fn skill_get_splash_(id: u16, lv: u16) -> i32 {
    skill_check(id).map(|s| lv_value(&s.splash, lv, (s.nk >> 32) as u32 as i32)).unwrap_or(0)
}
/// skill_get_splash: a negative splash means AREA_SIZE.
pub fn skill_get_splash(id: u16, lv: u16) -> i32 {
    let splash = skill_get_splash_(id, lv);
    if splash < 0 {
        return crate::conf::area_size() as i32;
    }
    splash
}
/// skill_get_num (level 0 reads `max`).
pub fn skill_get_num(id: u16, lv: u16) -> i32 {
    skill_check(id).map(|s| lv_value(&s.num, lv, s.max as i32)).unwrap_or(0)
}
/// skill_get_cast (level 0 reads the unused high word of `unit_flag`).
pub fn skill_get_cast(id: u16, lv: u16) -> i32 {
    skill_check(id).map(|s| lv_value(&s.cast, lv, (s.unit_flag >> 32) as u32 as i32)).unwrap_or(0)
}
/// skill_get_delay (level 0 reads cast[12]).
pub fn skill_get_delay(id: u16, lv: u16) -> i32 {
    skill_check(id).map(|s| lv_value(&s.delay, lv, s.cast[12])).unwrap_or(0)
}
/// skill_get_walkdelay (level 0 reads delay[12]).
pub fn skill_get_walkdelay(id: u16, lv: u16) -> i32 {
    skill_check(id).map(|s| lv_value(&s.walkdelay, lv, s.delay[12])).unwrap_or(0)
}
/// skill_get_time: Duration1 (level 0 reads walkdelay[12]).
pub fn skill_get_time(id: u16, lv: u16) -> i32 {
    skill_check(id).map(|s| lv_value(&s.upkeep_time, lv, s.walkdelay[12])).unwrap_or(0)
}
/// skill_get_time2: Duration2 (level 0 reads upkeep_time[12]).
pub fn skill_get_time2(id: u16, lv: u16) -> i32 {
    skill_check(id).map(|s| lv_value(&s.upkeep_time2, lv, s.upkeep_time[12])).unwrap_or(0)
}
pub fn skill_get_castdef(id: u16) -> i32 {
    skill_check(id).map(|s| s.cast_def_rate as i32).unwrap_or(0)
}
pub fn skill_get_castcancel(id: u16) -> i32 {
    skill_check(id).map(|s| s.castcancel as i32).unwrap_or(0)
}
/// skill_get_maxcount (level 0 reads the high word of `inf2`).
pub fn skill_get_maxcount(id: u16, lv: u16) -> i32 {
    skill_check(id).map(|s| lv_value(&s.maxcount, lv, (s.inf2 >> 32) as u32 as i32)).unwrap_or(0)
}
/// skill_get_blewcount (level 0 reads `skill_type`).
pub fn skill_get_blewcount(id: u16, lv: u16) -> i32 {
    skill_check(id).map(|s| lv_value(&s.blewcount, lv, s.skill_type as i32)).unwrap_or(0)
}
pub fn skill_get_castnodex(id: u16) -> i32 {
    skill_check(id).map(|s| s.castnodex as i32).unwrap_or(0)
}
pub fn skill_get_delaynodex(id: u16) -> i32 {
    skill_check(id).map(|s| s.delaynodex as i32).unwrap_or(0)
}
pub fn skill_get_nocast(id: u16) -> i32 {
    skill_check(id).map(|s| s.nocast as i32).unwrap_or(0)
}
/// skill_get_type: BF_* of the skill.
pub fn skill_get_type(id: u16) -> i32 {
    skill_check(id).map(|s| s.skill_type as i32).unwrap_or(0)
}
pub fn skill_get_unit_id(id: u16) -> i32 {
    skill_check(id).map(|s| s.unit_id as i32).unwrap_or(0)
}
pub fn skill_get_unit_id2(id: u16) -> i32 {
    skill_check(id).map(|s| s.unit_id2 as i32).unwrap_or(0)
}
pub fn skill_get_unit_interval(id: u16) -> i32 {
    skill_check(id).map(|s| s.unit_interval as i32).unwrap_or(0)
}
/// skill_get_unit_range (level 0 reads unit_layout_type[12]).
pub fn skill_get_unit_range(id: u16, lv: u16) -> i32 {
    skill_check(id).map(|s| lv_value(&s.unit_range, lv, s.unit_layout_type[12])).unwrap_or(0)
}
/// skill_get_unit_target: unit_target & BCT_ALL.
pub fn skill_get_unit_target(id: u16) -> i32 {
    skill_check(id).map(|s| s.unit_target & BCT_ALL).unwrap_or(0)
}
/// skill_get_unit_bl_target: unit_target & BL_ALL.
pub fn skill_get_unit_bl_target(id: u16) -> i32 {
    skill_check(id).map(|s| s.unit_target & BL_ALL).unwrap_or(0)
}
/// skill_get_unit_layout_type (level 0 reads unit_id | unit_id2 << 16).
pub fn skill_get_unit_layout_type(id: u16, lv: u16) -> i32 {
    skill_check(id)
        .map(|s| lv_value(&s.unit_layout_type, lv, (s.unit_id as u32 | (s.unit_id2 as u32) << 16) as i32))
        .unwrap_or(0)
}
/// skill_get_cooldown (level 0 reads upkeep_time2[12]).
pub fn skill_get_cooldown(id: u16, lv: u16) -> i32 {
    skill_check(id).map(|s| lv_value(&s.cooldown, lv, s.upkeep_time2[12])).unwrap_or(0)
}
/// skill_get_giveap (level 0 reads `nocast`).
pub fn skill_get_giveap(id: u16, lv: u16) -> i32 {
    skill_check(id).map(|s| lv_value(&s.giveap, lv, s.nocast as i32)).unwrap_or(0)
}
/// skill_get_fixed_cast (RENEWAL_CAST; level 0 reads cooldown[12]).
pub fn skill_get_fixed_cast(id: u16, lv: u16) -> i32 {
    skill_check(id).map(|s| lv_value(&s.fixed_cast, lv, s.cooldown[12])).unwrap_or(0)
}
/// skill_get_hp (level 0 reads the padding in front of `require`: 0).
pub fn skill_get_hp(id: u16, lv: u16) -> i32 {
    skill_check(id).map(|s| lv_value(&s.require.hp, lv, 0)).unwrap_or(0)
}
/// skill_get_mhp (level 0 reads hp[12]).
pub fn skill_get_mhp(id: u16, lv: u16) -> i32 {
    skill_check(id).map(|s| lv_value(&s.require.mhp, lv, s.require.hp[12])).unwrap_or(0)
}
/// skill_get_sp (level 0 reads mhp[12]).
pub fn skill_get_sp(id: u16, lv: u16) -> i32 {
    skill_check(id).map(|s| lv_value(&s.require.sp, lv, s.require.mhp[12])).unwrap_or(0)
}
/// skill_get_ap (level 0 reads sp[12]).
pub fn skill_get_ap(id: u16, lv: u16) -> i32 {
    skill_check(id).map(|s| lv_value(&s.require.ap, lv, s.require.sp[12])).unwrap_or(0)
}
/// skill_get_hp_rate (level 0 reads ap[12]).
pub fn skill_get_hp_rate(id: u16, lv: u16) -> i32 {
    skill_check(id).map(|s| lv_value(&s.require.hp_rate, lv, s.require.ap[12])).unwrap_or(0)
}
/// skill_get_sp_rate (level 0 reads hp_rate[12]).
pub fn skill_get_sp_rate(id: u16, lv: u16) -> i32 {
    skill_check(id).map(|s| lv_value(&s.require.sp_rate, lv, s.require.hp_rate[12])).unwrap_or(0)
}
/// skill_get_ap_rate (level 0 reads sp_rate[12]).
pub fn skill_get_ap_rate(id: u16, lv: u16) -> i32 {
    skill_check(id).map(|s| lv_value(&s.require.ap_rate, lv, s.require.sp_rate[12])).unwrap_or(0)
}
/// skill_get_zeny (level 0 reads ap_rate[12]).
pub fn skill_get_zeny(id: u16, lv: u16) -> i32 {
    skill_check(id).map(|s| lv_value(&s.require.zeny, lv, s.require.ap_rate[12])).unwrap_or(0)
}
pub fn skill_get_weapontype(id: u16) -> i32 {
    skill_check(id).map(|s| s.require.weapon).unwrap_or(0)
}
pub fn skill_get_ammotype(id: u16) -> i32 {
    skill_check(id).map(|s| s.require.ammo).unwrap_or(0)
}
/// skill_get_ammo_qty (level 0 reads `ammo`).
pub fn skill_get_ammo_qty(id: u16, lv: u16) -> i32 {
    skill_check(id).map(|s| lv_value(&s.require.ammo_qty, lv, s.require.ammo)).unwrap_or(0)
}
pub fn skill_get_state(id: u16) -> i32 {
    skill_check(id).map(|s| s.require.state).unwrap_or(0)
}
pub fn skill_get_status_count(id: u16) -> usize {
    skill_check(id).map(|s| s.require.status.len()).unwrap_or(0)
}
/// skill_get_spiritball (level 0 reads `state`).
pub fn skill_get_spiritball(id: u16, lv: u16) -> i32 {
    skill_check(id).map(|s| lv_value(&s.require.spiritball, lv, s.require.state)).unwrap_or(0)
}
/// skill_get_sc: SC_NONE for an unknown skill.
pub fn skill_get_sc(id: u16) -> i16 {
    skill_check(id).map(|s| s.sc).unwrap_or(SC_NONE)
}

/// skill.hpp `skill_get_nk(skill_id, nk)` / skill_get_nk_: any of the NK_* bits (no index check, only find).
pub fn skill_get_nk(skill_id: u16, nk: &[u8]) -> bool {
    if skill_id == 0 {
        return false;
    }
    skill_db().find(skill_id).map(|s| nk.iter().any(|&b| s.has_nk(b))).unwrap_or(false)
}
/// skill.hpp `skill_get_inf2(skill_id, inf2)` / skill_get_inf2_: any of the INF2_* bits.
pub fn skill_get_inf2(skill_id: u16, inf2: &[u8]) -> bool {
    if skill_id == 0 {
        return false;
    }
    skill_db().find(skill_id).map(|s| inf2.iter().any(|&b| s.has_inf2(b))).unwrap_or(false)
}
/// skill.hpp `skill_get_unit_flag(skill_id, unit)` / skill_get_unit_flag_: any of the UF_* bits.
pub fn skill_get_unit_flag(skill_id: u16, unit: &[u8]) -> bool {
    if skill_id == 0 {
        return false;
    }
    skill_db().find(skill_id).map(|s| unit.iter().any(|&b| s.has_unit_flag(b))).unwrap_or(false)
}

/// skill.cpp skill_get_casttype: ground / castend damage / castend no-damage (an unknown skill is CAST_DAMAGE).
pub fn skill_get_casttype(skill_id: u16) -> CastType {
    let Some(skill) = skill_db().find(skill_id) else {
        return CastType::Damage;
    };
    if skill.inf & INF_GROUND_SKILL != 0 {
        return CastType::Ground;
    }
    if skill.inf & INF_SUPPORT_SKILL != 0 {
        return CastType::NoDamage;
    }
    if skill.inf & INF_SELF_SKILL != 0 {
        if skill.has_inf2(INF2_NOTARGETSELF) {
            return CastType::Damage; // combo skill
        }
        return CastType::NoDamage;
    }
    if skill.has_nk(NK_NODAMAGE) {
        return CastType::NoDamage;
    }
    CastType::Damage
}

/// skill.cpp skill_tree_get_max: the class's skill tree MaxLevel, else the skill's MaxLevel.
pub fn skill_tree_get_max(skill_id: u16, b_class: i32) -> i32 {
    match crate::db::jobs::skill_tree_db().get_skill_data(b_class, skill_id) {
        Some(e) => e.max_lv as i32,
        None => skill_get_max(skill_id),
    }
}

/// mmo.hpp HM_SKILLBASE / MAX_HOMUNSKILL, MC_SKILLBASE / MAX_MERCSKILL, EL_SKILLBASE / MAX_ELEMENTALSKILL,
/// ABR_SKILLBASE / MAX_ABRSKILL.
pub const HM_SKILLBASE: u16 = 8001;
pub const MAX_HOMUNSKILL: u16 = 59;
pub const MC_SKILLBASE: u16 = 8201;
pub const MAX_MERCSKILL: u16 = 41;
pub const EL_SKILLBASE: u16 = 8401;
pub const MAX_ELEMENTALSKILL: u16 = 57;
pub const ABR_SKILLBASE: u16 = 8601;
pub const MAX_ABRSKILL: u16 = 5;

/// skill.hpp SKILL_CHK_HOMUN / MERC / ELEM / ABR / GUILD.
pub fn skill_chk_homun(id: u16) -> bool {
    (HM_SKILLBASE..HM_SKILLBASE + MAX_HOMUNSKILL).contains(&id)
}
pub fn skill_chk_merc(id: u16) -> bool {
    (MC_SKILLBASE..MC_SKILLBASE + MAX_MERCSKILL).contains(&id)
}
pub fn skill_chk_elem(id: u16) -> bool {
    (EL_SKILLBASE..EL_SKILLBASE + MAX_ELEMENTALSKILL).contains(&id)
}
pub fn skill_chk_abr(id: u16) -> bool {
    (ABR_SKILLBASE..ABR_SKILLBASE + MAX_ABRSKILL).contains(&id)
}
pub fn skill_chk_guild(id: u16) -> bool {
    (GD_SKILLBASE..GD_SKILLBASE + MAX_GUILDSKILL).contains(&id)
}

// ---------------------------------------------------------------------------------------------- produce / change material

/// skill.hpp s_skill_produce_db (produce_db.txt; the row id is the array index).
#[derive(Deserialize, Clone, Debug)]
pub struct SkillProduce {
    /// ProduceItemID (0: empty slot).
    pub nameid: u32,
    /// RequireSkill.
    pub req_skill: u16,
    /// Requireskill_lv.
    pub req_skill_lv: u8,
    /// ItemLV.
    pub itemlv: u8,
    /// MaterialID1..12.
    pub mat_id: [u32; MAX_PRODUCE_RESOURCE],
    /// MaterialAmount1..12.
    pub mat_amount: [u16; MAX_PRODUCE_RESOURCE],
}

/// skill.cpp s_skill_changematerial_db (skill_changematerial_db.txt; the row id is the array index).
#[derive(Deserialize, Clone, Debug)]
pub struct SkillChangeMaterial {
    /// ProductID (0: empty slot).
    pub nameid: u32,
    /// BaseRate.
    pub rate: u16,
    /// MakeAmount1..3.
    pub qty: [u16; MAX_SKILL_CHANGEMATERIAL_SET],
    /// MakeAmountRate1..3.
    pub qty_rate: [u16; MAX_SKILL_CHANGEMATERIAL_SET],
}

#[derive(Deserialize)]
pub struct ProduceDatabases {
    /// skill_produce_db[MAX_SKILL_PRODUCE_DB].
    pub produce: Vec<SkillProduce>,
    /// skill_changematerial_db[MAX_SKILL_CHANGEMATERIAL_DB].
    pub changematerial: Vec<SkillChangeMaterial>,
    /// (file, rows) as sv_readdb's "Done reading" lines report them.
    pub loaded: Vec<(String, u32)>,
}

crate::dataset!(produce_dbs, "skills_produce", ProduceDatabases);

/// skill.cpp skill_produce_db.
pub fn skill_produce_db() -> &'static [SkillProduce] {
    &produce_dbs().produce
}
/// skill.cpp skill_changematerial_db.
pub fn skill_changematerial_db() -> &'static [SkillChangeMaterial] {
    &produce_dbs().changematerial
}

// ---------------------------------------------------------------------------------------------- abra, mushroom, spellbook, arrows, guild

/// skill.hpp s_skill_abra_db.
#[derive(Deserialize, Clone, Debug)]
pub struct SkillAbra {
    /// Skill (AEGIS name resolved).
    pub skill_id: u16,
    /// Probability per level (1 = 0.01%; default 500 for every level; a Level list zeroes unlisted levels).
    pub per: [u16; MAX_SKILL_LEVEL],
}

/// skill.hpp s_skill_spellbook_db.
#[derive(Deserialize, Clone, Debug)]
pub struct SkillSpellbook {
    /// Skill.
    pub skill_id: u16,
    /// PreservePoints.
    pub points: u16,
    /// Book (item id).
    pub nameid: u32,
}

/// skill.hpp s_skill_arrow_db.
#[derive(Deserialize, Clone, Debug)]
pub struct SkillArrow {
    /// Source (item id).
    pub nameid: u32,
    /// Make: (item id, amount), in the unordered_map's iteration order.
    pub created: Vec<(u32, u16)>,
}

/// guild.cpp s_guild_skill_tree.
#[derive(Deserialize, Clone, Debug)]
pub struct GuildSkillTree {
    /// Id.
    pub id: u16,
    /// MaxLevel (GD_GLORYGUILD 0 becomes 1 with require_glory_guild).
    pub max: u16,
    /// Required: (skill id, level), in the unordered_map's iteration order.
    pub need: Vec<(u16, u16)>,
}

#[derive(Deserialize)]
pub struct SkillMisc {
    /// AbraDatabase in iteration order (AbraDatabase::random picks by position).
    pub abra: Vec<SkillAbra>,
    /// MagicMushroomDatabase skill ids in iteration order.
    pub magic_mushroom: Vec<u16>,
    /// ReadingSpellbookDatabase in iteration order.
    pub spellbook: Vec<SkillSpellbook>,
    /// SkillArrowDatabase in iteration order.
    pub arrow: Vec<SkillArrow>,
    /// GuildSkillTreeDatabase in iteration order.
    pub guild_skill_tree: Vec<GuildSkillTree>,
    /// (file, entries) as rAthena's "Done reading" lines report them.
    pub loaded: Vec<(String, u32)>,
}

crate::dataset!(skill_misc, "skills_misc", SkillMisc);

/// AbraDatabase::find.
pub fn abra_find(skill_id: u16) -> Option<&'static SkillAbra> {
    skill_misc().abra.iter().find(|a| a.skill_id == skill_id)
}
/// abra_db / magic_mushroom_db `random()` (util::umap_random): the entry `rnd_value(0, size - 1)` places from the start.
pub fn abra_random(pos: usize) -> Option<&'static SkillAbra> {
    skill_misc().abra.get(pos)
}
/// MagicMushroomDatabase::find.
pub fn magic_mushroom_exists(skill_id: u16) -> bool {
    skill_misc().magic_mushroom.contains(&skill_id)
}
/// ReadingSpellbookDatabase::find (by skill).
pub fn reading_spellbook_find(skill_id: u16) -> Option<&'static SkillSpellbook> {
    skill_misc().spellbook.iter().find(|s| s.skill_id == skill_id)
}
/// ReadingSpellbookDatabase::findBook: the first spell (iteration order) of that book; `item_exists` is rAthena's
/// item_db.exists(nameid) guard.
pub fn reading_spellbook_find_book(nameid: u32, item_exists: bool) -> Option<&'static SkillSpellbook> {
    let db = &skill_misc().spellbook;
    if !item_exists || db.is_empty() {
        return None;
    }
    db.iter().find(|s| s.nameid == nameid)
}
/// SkillArrowDatabase::find (by source item).
pub fn skill_arrow_find(nameid: u32) -> Option<&'static SkillArrow> {
    skill_misc().arrow.iter().find(|a| a.nameid == nameid)
}
/// guild.cpp guild_skill_tree_db.find.
pub fn guild_skill_tree_find(id: u16) -> Option<&'static GuildSkillTree> {
    skill_misc().guild_skill_tree.iter().find(|g| g.id == id)
}
/// guild.cpp guild_skill_get_max.
pub fn guild_skill_get_max(id: u16) -> u16 {
    guild_skill_tree_find(id).map(|g| g.max).unwrap_or(0)
}
/// guild.cpp guild_skill_get_index: index in mmo_guild::skill, or -1.
pub fn guild_skill_get_index(skill_id: u16) -> i16 {
    if !skill_chk_guild(skill_id) {
        return -1;
    }
    let idx = skill_id - GD_SKILLBASE;
    if idx >= MAX_GUILDSKILL {
        return -1;
    }
    idx as i16
}
/// guild.cpp guild_check_skill_require: every Required level is met (`checkskill` = guild_checkskill of the guild).
pub fn guild_check_skill_require(id: u16, checkskill: impl Fn(u16) -> i32) -> bool {
    let Some(skill) = guild_skill_tree_find(id) else {
        return false;
    };
    skill.need.iter().all(|&(req, lv)| lv as i32 <= checkskill(req))
}

// ---------------------------------------------------------------------------------------------- unit layouts

/// skill.hpp s_skill_unit_layout / s_skill_nounit_layout.
#[derive(Deserialize, Clone, Debug)]
pub struct SkillUnitLayout {
    pub count: i32,
    pub dx: Vec<i32>,
    pub dy: Vec<i32>,
}

/// skill.cpp skill_init_unit_layout / skill_init_nounit_layout results.
#[derive(Deserialize)]
pub struct SkillUnitLayouts {
    /// skill_unit_layout[MAX_SKILL_UNIT_LAYOUT]: squares 0..=7, the special layouts (in skill_db iteration order),
    /// then 8 directions each of firewall, icewall, earthstrain and fire rain.
    pub layout: Vec<SkillUnitLayout>,
    pub firewall_unit_pos: i32,
    pub icewall_unit_pos: i32,
    pub earthstrain_unit_pos: i32,
    pub firerain_unit_pos: i32,
    /// Never assigned in rAthena (0); GN_WALLOFTHORN gets its own special layout instead.
    pub wallofthorn_unit_pos: i32,
    /// skill_nounit_layout[MAX_SKILL_UNIT_LAYOUT2].
    pub nounit_layout: Vec<SkillUnitLayout>,
}

crate::dataset!(skill_unit_layouts, "skills_unit_layout", SkillUnitLayouts);

/// skill.cpp skill_get_unit_layout. `src_is_mob`: src->type == BL_MOB; `dir`: 6 when the target cell is the caster's
/// own, else map_calc_dir(src, x, y) (the caller computes it).
pub fn skill_get_unit_layout(skill_id: u16, skill_lv: u16, src_is_mob: bool, dir: u8) -> &'static SkillUnitLayout {
    let l = skill_unit_layouts();
    let mut pos = skill_get_unit_layout_type(skill_id, skill_lv);
    if pos < -1 || pos >= MAX_SKILL_UNIT_LAYOUT {
        log::error!("skill_get_unit_layout: unsupported layout type {pos} for skill {skill_id} (level {skill_lv})");
        pos = pos.clamp(0, MAX_SQUARE_LAYOUT);
    }
    // Monsters sometimes deploy more units on level 10
    if src_is_mob && skill_lv >= 10 && skill_id == WZ_WATERBALL {
        pos = 4; // 9x9 area
    }
    if pos != -1 {
        return &l.layout[pos as usize];
    }
    let d = dir as i32;
    let at = |base: i32| &l.layout[(base + d) as usize];
    match skill_id {
        MG_FIREWALL => at(l.firewall_unit_pos),
        WZ_ICEWALL => at(l.icewall_unit_pos),
        WL_EARTHSTRAIN => at(l.earthstrain_unit_pos),
        RL_FIRE_RAIN => at(l.firerain_unit_pos),
        GN_WALLOFTHORN => at(l.wallofthorn_unit_pos),
        _ => {
            log::error!("skill_get_unit_layout: unknown unit layout for skill {skill_id} (level {skill_lv})");
            &l.layout[0]
        }
    }
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
            loaded(&skill_db().loaded),
            vec![
                ("db/re/skill_db.yml", 1635),
                ("db/import/skill_db.yml", 1),
                ("db/re/skill_nocast_db.txt", 158),
                ("db/skill_damage_db.txt", 0),
                ("db/import/skill_nocast_db.txt", 0),
                ("db/import/skill_damage_db.txt", 0),
            ]
        );
        assert_eq!(skill_db().list.len(), 1635);
        assert_eq!(skill_db().skill_num, 1636);
        assert_eq!(
            loaded(&produce_dbs().loaded),
            vec![
                ("db/re/produce_db.txt", 293),
                ("db/skill_changematerial_db.txt", 74),
                ("db/import/produce_db.txt", 0),
                ("db/import/skill_changematerial_db.txt", 0),
            ]
        );
        assert_eq!(skill_produce_db().len(), MAX_SKILL_PRODUCE_DB);
        assert_eq!(skill_changematerial_db().iter().filter(|c| c.nameid != 0).count(), 74);
        let m = skill_misc();
        assert_eq!(
            loaded(&m.loaded),
            vec![
                ("db/abra_db.yml", 155),
                ("db/re/magicmushroom_db.yml", 23),
                ("db/re/spellbook_db.yml", 10),
                ("db/create_arrow_db.yml", 136),
                ("db/re/guild_skill_tree.yml", 19),
            ]
        );
        assert_eq!(
            (m.abra.len(), m.magic_mushroom.len(), m.spellbook.len(), m.arrow.len(), m.guild_skill_tree.len()),
            (155, 23, 10, 136, 19)
        );
    }

    #[test]
    fn nv_basic() {
        let s = skill_db().find(NV_BASIC).unwrap();
        assert_eq!(s.name, "NV_BASIC");
        assert_eq!(s.desc, "Basic Skill");
        assert_eq!(skill_get_index(NV_BASIC), 1);
        assert_eq!(skill_get_max(NV_BASIC), 9);
        assert_eq!(skill_get_inf(NV_BASIC), INF_PASSIVE_SKILL as i32);
        assert!(s.castcancel);
        assert_eq!(skill_get_sc(NV_BASIC), SC_NONE);
        assert_eq!(skill_get_ele(NV_BASIC, 1), 0);
        assert_eq!(skill_get_range(NV_BASIC, 5), 0);
        assert_eq!(skill_name2id("nv_basic"), NV_BASIC);
        assert_eq!(skill_get_casttype(NV_BASIC), CastType::Damage);
    }

    #[test]
    fn al_heal_per_level() {
        assert_eq!(AL_HEAL, 28);
        let s = skill_db().find(AL_HEAL).unwrap();
        assert_eq!(s.desc, "Heal");
        assert_eq!(skill_get_max(AL_HEAL), 10);
        assert_eq!(skill_get_inf(AL_HEAL), INF_SUPPORT_SKILL as i32);
        assert_eq!(skill_get_hit(AL_HEAL), 6); // DMG_SINGLE
        assert_eq!(skill_get_type(AL_HEAL), 2); // BF_MAGIC
        assert!(skill_get_nk(AL_HEAL, &[NK_NODAMAGE]));
        assert!(skill_get_nk(AL_HEAL, &[NK_IGNOREDEFENSE]));
        assert!(!skill_get_nk(AL_HEAL, &[NK_SPLASH, NK_CRITICAL]));
        assert!(skill_get_inf2(AL_HEAL, &[INF2_ISAUTOSHADOWSPELL]));
        assert_eq!(s.copyable.option, SKILL_COPY_PLAGIARISM | SKILL_COPY_REPRODUCE);
        assert_eq!(skill_get_ele(AL_HEAL, 1), 6); // ELE_HOLY
        for lv in 1..=10u16 {
            assert_eq!(skill_get_range(AL_HEAL, lv), 9);
            assert_eq!(skill_get_sp(AL_HEAL, lv), 10 + 3 * lv as i32);
            assert_eq!(skill_get_delay(AL_HEAL, lv), 500);
            assert_eq!(skill_get_num(AL_HEAL, lv), 1);
            assert_eq!(skill_get_cast(AL_HEAL, lv), 0);
        }
        // parseNode's trend continues to level 13; skill_get_lv extrapolates above it
        assert_eq!(s.require.sp[10..], [43, 46, 49]);
        assert_eq!(skill_get_sp(AL_HEAL, 14), 49 + (2 * 3) / 2 + (3) / 2);
        assert_eq!(skill_get_casttype(AL_HEAL), CastType::NoDamage);
        // level 0 reads mhp[12] in rAthena (0 for Heal)
        assert_eq!(skill_get_sp(AL_HEAL, 0), 0);
    }

    #[test]
    fn unknown_skills() {
        assert_eq!(skill_get_index(0), 0);
        assert_eq!(skill_get_max(0), 0);
        assert_eq!(skill_get_hit(65000), DMG_NORMAL);
        assert_eq!(skill_get_sc(65000), SC_NONE);
        assert_eq!(skill_get_casttype(65000), CastType::Damage);
        assert_eq!(skill_name2id("NO_SUCH_SKILL"), 0);
    }

    #[test]
    fn layouts() {
        let l = skill_unit_layouts();
        assert_eq!(l.layout.len(), MAX_SKILL_UNIT_LAYOUT as usize);
        assert_eq!(l.layout[0].count, 1);
        assert_eq!(l.layout[7].count, 225);
        assert_eq!(skill_get_unit_layout(PR_SANCTUARY, 1, false, 0).count, 21);
        assert_eq!(skill_get_unit_layout(MG_FIREWALL, 1, false, 0).count, 3);
        assert_eq!(skill_get_unit_layout(MG_FIREWALL, 1, false, 1).count, 5);
        assert_eq!(skill_get_unit_layout(NJ_TATAMIGAESHI, 1, false, 0).count, 4);
        assert_eq!(skill_get_unit_layout(NJ_TATAMIGAESHI, 3, false, 0).count, 8);
        assert_eq!(skill_get_unit_layout(NJ_TATAMIGAESHI, 5, false, 0).count, 12);
        assert_eq!(l.nounit_layout.len(), MAX_SKILL_UNIT_LAYOUT2);
    }

    #[test]
    fn guild_tree() {
        assert_eq!(guild_skill_get_index(GD_APPROVAL), 0);
        assert_eq!(guild_skill_get_index(GD_EMERGENCY_MOVE), 19);
        assert_eq!(guild_skill_get_max(GD_APPROVAL), 1);
        assert!(guild_check_skill_require(GD_APPROVAL, |_| 0));
    }

    /// Cross-check tool, not a regular test: `LOKI_SKILL_ACC=<file> cargo test --lib dump_accessors -- --ignored`
    /// writes the accessors' results (levels 0 1 7 13 14 20 100) for every skill, 0 and 65000, to compare with the
    /// oracle's own skill_get_* called through gdb on a map-server stopped after do_init_guild.
    #[test]
    #[ignore]
    fn dump_accessors() {
        let Ok(path) = std::env::var("LOKI_SKILL_ACC") else { return };
        let lv_fns: [(&str, fn(u16, u16) -> i32); 26] = [
            ("skill_get_ele", skill_get_ele), ("skill_get_range", skill_get_range), ("skill_get_splash", skill_get_splash),
            ("skill_get_num", skill_get_num), ("skill_get_cast", skill_get_cast), ("skill_get_delay", skill_get_delay),
            ("skill_get_walkdelay", skill_get_walkdelay), ("skill_get_time", skill_get_time), ("skill_get_time2", skill_get_time2),
            ("skill_get_maxcount", skill_get_maxcount), ("skill_get_blewcount", skill_get_blewcount),
            ("skill_get_unit_range", skill_get_unit_range), ("skill_get_unit_layout_type", skill_get_unit_layout_type),
            ("skill_get_cooldown", skill_get_cooldown), ("skill_get_giveap", skill_get_giveap),
            ("skill_get_fixed_cast", skill_get_fixed_cast), ("skill_get_hp", skill_get_hp), ("skill_get_mhp", skill_get_mhp),
            ("skill_get_sp", skill_get_sp), ("skill_get_ap", skill_get_ap), ("skill_get_hp_rate", skill_get_hp_rate),
            ("skill_get_sp_rate", skill_get_sp_rate), ("skill_get_ap_rate", skill_get_ap_rate), ("skill_get_zeny", skill_get_zeny),
            ("skill_get_ammo_qty", skill_get_ammo_qty), ("skill_get_spiritball", skill_get_spiritball),
        ];
        let fns: [(&str, fn(u16) -> i32); 16] = [
            ("skill_get_inf", skill_get_inf), ("skill_get_max", skill_get_max), ("skill_get_castdef", skill_get_castdef),
            ("skill_get_castcancel", skill_get_castcancel), ("skill_get_castnodex", skill_get_castnodex),
            ("skill_get_delaynodex", skill_get_delaynodex), ("skill_get_nocast", skill_get_nocast),
            ("skill_get_type", skill_get_type), ("skill_get_unit_id", skill_get_unit_id), ("skill_get_unit_id2", skill_get_unit_id2),
            ("skill_get_unit_interval", skill_get_unit_interval), ("skill_get_unit_target", skill_get_unit_target),
            ("skill_get_unit_bl_target", skill_get_unit_bl_target), ("skill_get_weapontype", skill_get_weapontype),
            ("skill_get_ammotype", skill_get_ammotype), ("skill_get_state", skill_get_state),
        ];
        let mut ids: Vec<u16> = skill_db().iter().map(|s| s.nameid).collect();
        ids.extend([0, 65000]);
        let mut out = serde_json::Map::new();
        for id in ids {
            let mut r = serde_json::Map::new();
            for (n, f) in lv_fns {
                r.insert(n.into(), [0u16, 1, 7, 13, 14, 20, 100].iter().map(|&lv| f(id, lv)).collect::<Vec<_>>().into());
            }
            for (n, f) in fns {
                r.insert(n.into(), f(id).into());
            }
            r.insert("skill_get_hit".into(), (skill_get_hit(id) as i32).into());
            r.insert("skill_get_sc".into(), (skill_get_sc(id) as i32).into());
            r.insert("skill_get_casttype".into(), (skill_get_casttype(id) as i32).into());
            r.insert("skill_dummy2skill_id".into(), (skill_dummy2skill_id(id) as i32).into());
            r.insert("skill_get_index_".into(), (skill_db().get_index(id, true) as i32).into());
            r.insert("skill_tree_get_max_knight".into(), skill_tree_get_max(id, crate::db::jobs::JOB_KNIGHT).into());
            out.insert(id.to_string(), r.into());
        }
        std::fs::write(path, serde_json::Value::Object(out).to_string()).unwrap();
    }
}
