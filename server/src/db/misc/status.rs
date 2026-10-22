//! status.cpp: StatusDatabase (db/status.yml -> db/re/status.yml -> import), SCDisabled (status_readdb_status_disabled
//! over db/status_disabled.txt + import), SizeFixDatabase and AttributeDatabase.

use std::collections::HashMap;

use serde::Deserialize;

/// status.hpp sc_type bounds (SC_NONE -1, SC_MAX from the compiler).
pub const SC_NONE: i32 = -1;
pub const SC_MAX: i32 = 1044;
/// SC_COMMON_MAX: statuses above it belong to a job's skill tree (changeSkillTree).
pub const SC_COMMON_MAX: i32 = 11;
/// efst_type bounds.
pub const EFST_BLANK: i32 = -1;
pub const EFST_MAX: i32 = 1689;
/// Sizes of the bitsets (e_scb_flag, e_scs_flag, e_status_change_flag).
pub const SCB_MAX: u32 = 45;
pub const SCS_MAX: u32 = 25;
pub const SCF_MAX: u32 = 53;
/// e_scb_flag members the database constructor names.
pub const SCB_NONE: u32 = 0;
pub const SCB_BASE: u32 = 1;
pub const SCB_DYE: u32 = 44;
/// map.hpp BL_PC and status.hpp BL_SCEFFECT (BL_CHAR, the units a status-change effect is shown for).
pub const BL_PC: u16 = 0x001;
pub const BL_SCEFFECT: u16 = 0x21B;

/// status.hpp struct s_status_change_db.
#[derive(Deserialize, Clone, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct StatusChangeDb {
    /// `Status` (SC_ constant).
    #[serde(rename = "type")]
    pub type_: i32,
    /// `Icon` (EFST_ constant; EFST_BLANK when absent, invalid, or SC_HALLUCINATION with display_hallucination off).
    pub icon: i32,
    /// `States`: std::bitset<SCS_MAX> (bit n = e_scs_flag n).
    pub state: u64,
    /// `CalcFlags`: std::bitset<SCB_MAX> (`All` is SCB_ALL; a Script adds SCB_ALL in loadingFinished).
    pub calc_flag: u64,
    /// `Opt1` (OPT1_ value).
    pub opt1: u16,
    /// `Opt2` (OPT2_ bits).
    pub opt2: u16,
    /// `Opt3` (OPT3_ bits).
    pub opt3: u32,
    /// `Options` (OPTION_ bits, the change look).
    pub look: u32,
    /// `Flags`: std::bitset<SCF_MAX> (bit n = e_status_change_flag n).
    pub flag: u64,
    /// `DurationLookup` (skill_name2id; 0 none).
    pub skill_id: u16,
    /// `EndOnStart`.
    pub endonstart: Vec<i32>,
    /// `Fail`.
    pub fail: Vec<i32>,
    /// `EndReturn`.
    pub endreturn: Vec<i32>,
    /// `EndOnEnd`.
    pub endonend: Vec<i32>,
    /// `MinDuration` (default 1).
    pub min_duration: i64,
    /// `MinRate` (default 0).
    pub min_rate: u16,
    /// `Script` (source text).
    pub script: Option<String>,
}

impl StatusChangeDb {
    /// `state[flag]` (e_scs_flag).
    pub fn has_state(&self, scs: u32) -> bool {
        scs < 64 && self.state & (1u64 << scs) != 0
    }
    /// `calc_flag[flag]` (e_scb_flag).
    pub fn has_calc_flag(&self, scb: u32) -> bool {
        scb < 64 && self.calc_flag & (1u64 << scb) != 0
    }
    /// `flag[flag]` (e_status_change_flag).
    pub fn has_flag(&self, scf: u32) -> bool {
        scf < 64 && self.flag & (1u64 << scf) != 0
    }
}

#[derive(Deserialize)]
struct StatusFile {
    statuses: Vec<StatusChangeDb>,
    relevant_bl_types: Vec<(u16, u16)>,
    disabled: Vec<(i32, u16)>,
    scb_all: u64,
    scb_battle: u64,
}

/// StatusDatabase plus the tables status.cpp keeps beside it (StatusRelevantBLTypes, SCDisabled).
#[derive(Deserialize)]
#[serde(from = "StatusFile")]
pub struct StatusDatabase {
    list: Vec<StatusChangeDb>,
    index: HashMap<i32, usize>,
    relevant_bl: HashMap<u16, u16>,
    disabled: HashMap<i32, u16>,
    scb_all: u64,
    scb_battle: u64,
}

impl From<StatusFile> for StatusDatabase {
    fn from(f: StatusFile) -> Self {
        StatusDatabase {
            index: f.statuses.iter().enumerate().map(|(i, s)| (s.type_, i)).collect(),
            list: f.statuses,
            relevant_bl: f.relevant_bl_types.into_iter().collect(),
            disabled: f.disabled.into_iter().collect(),
            scb_all: f.scb_all,
            scb_battle: f.scb_battle,
        }
    }
}

impl StatusDatabase {
    /// status_db.find (the database is keyed by uint16).
    pub fn find(&self, sc: i32) -> Option<&StatusChangeDb> {
        self.index.get(&(sc as u16 as i32)).map(|&i| &self.list[i])
    }
    pub fn len(&self) -> usize {
        self.list.len()
    }
    pub fn is_empty(&self) -> bool {
        self.list.is_empty()
    }
    /// `for (auto& it : status_db)`: rAthena's iteration order.
    pub fn iter(&self) -> impl Iterator<Item = &StatusChangeDb> {
        self.list.iter()
    }
    /// StatusDatabase::getIcon.
    pub fn get_icon(&self, sc: i32) -> i32 {
        self.find(sc).map(|s| s.icon).unwrap_or(EFST_BLANK)
    }
    /// StatusDatabase::getCalcFlag (bitset as bits).
    pub fn get_calc_flag(&self, sc: i32) -> u64 {
        self.find(sc).map(|s| s.calc_flag).unwrap_or(0)
    }
    /// StatusDatabase::getEndOnStart.
    pub fn get_end_on_start(&self, sc: i32) -> &[i32] {
        self.find(sc).map(|s| s.endonstart.as_slice()).unwrap_or(&[])
    }
    /// StatusDatabase::getSkill.
    pub fn get_skill(&self, sc: i32) -> u16 {
        self.find(sc).map(|s| s.skill_id).unwrap_or(0)
    }
    /// StatusDatabase::getSCB_BATTLE (every SCB but SCB_BASE and SCB_DYE).
    pub fn get_scb_battle(&self) -> u64 {
        self.scb_battle
    }
    /// StatusDatabase::getSCB_ALL (every SCB but SCB_DYE).
    pub fn get_scb_all(&self) -> u64 {
        self.scb_all
    }
    /// StatusDatabase::validateStatus.
    pub fn validate_status(sc: i32) -> bool {
        sc > SC_NONE && sc < SC_MAX
    }
    /// StatusDatabase::hasSCF: some status the unit has (`has_sc`) carries `flag` (SCF_NONE never matches).
    pub fn has_scf(&self, has_sc: &dyn Fn(i32) -> bool, flag: u32) -> bool {
        if flag == 0 {
            return false;
        }
        self.list.iter().any(|s| has_sc(s.type_) && s.has_flag(flag))
    }
    /// StatusDatabase::removeByStatusFlag: the status_change_end calls it makes, in order (a status matching several
    /// flags is ended once per flag; the later calls find nothing to end).
    pub fn remove_by_status_flag(&self, has_sc: &dyn Fn(i32) -> bool, flags: &[u32]) -> Vec<i32> {
        let mut out = Vec::new();
        for s in &self.list {
            if has_sc(s.type_) {
                for &f in flags {
                    if s.has_flag(f) {
                        out.push(s.type_);
                    }
                }
            }
        }
        out
    }
    /// status.cpp status_efst_get_bl_type (StatusRelevantBLTypes).
    pub fn status_efst_get_bl_type(&self, efst: i32) -> u16 {
        if efst <= EFST_BLANK || efst >= EFST_MAX {
            return BL_PC;
        }
        self.relevant_bl.get(&(efst as u16)).copied().unwrap_or(BL_PC)
    }
    /// status.cpp SCDisabled[type] (uint16; status_disabled.txt: 1 normal maps, 2 PVP, 4 GVG, 8 BG, 16 TE, other
    /// bits are map zones).
    pub fn sc_disabled(&self, sc: i32) -> u16 {
        self.disabled.get(&sc).copied().unwrap_or(0)
    }
}

crate::dataset!(status_db, "misc_status", StatusDatabase);

/// script_get_constant("SC_<name>") for a status name as status.yml writes it (e.g. "Blessing").
pub fn sc_by_name(name: &str) -> Option<i32> {
    crate::data::constant(&format!("SC_{name}")).map(|v| v as i32)
}

// ------------------------------------------------------------------------------------------------------ size_fix

/// status.hpp struct s_sizefix_db.
#[derive(Deserialize, Clone, Copy, Debug, Default, PartialEq, Eq)]
#[serde(default)]
pub struct SizeFix {
    /// `Small` (default 100).
    pub small: u16,
    /// `Medium` (default 100).
    pub medium: u16,
    /// `Large` (default 100).
    pub large: u16,
}

#[derive(Deserialize)]
struct SizeFixFile {
    weapons: Vec<(i32, SizeFix)>,
}

/// SizeFixDatabase (weapon type -> s_sizefix_db).
#[derive(Deserialize)]
#[serde(from = "SizeFixFile")]
pub struct SizeFixDatabase {
    by_weapon: HashMap<i32, SizeFix>,
}

impl From<SizeFixFile> for SizeFixDatabase {
    fn from(f: SizeFixFile) -> Self {
        SizeFixDatabase { by_weapon: f.weapons.into_iter().collect() }
    }
}

impl SizeFixDatabase {
    pub fn find(&self, weapon: i32) -> Option<&SizeFix> {
        self.by_weapon.get(&weapon)
    }
    pub fn len(&self) -> usize {
        self.by_weapon.len()
    }
    pub fn is_empty(&self) -> bool {
        self.by_weapon.is_empty()
    }
}

crate::dataset!(size_fix_db, "misc_size_fix", SizeFixDatabase);

// ------------------------------------------------------------------------------------------------------ attr_fix

/// map.hpp MAX_ELE_LEVEL and ELE_MAX.
pub const MAX_ELE_LEVEL: usize = 4;
pub const ELE_MAX: usize = 11;

/// AttributeDatabase: attr_fix_table[level-1][atk_ele][def_ele] (100 where attr_fix.yml sets nothing).
#[derive(Deserialize)]
pub struct AttributeDatabase {
    pub attr_fix_table: [[[i16; ELE_MAX]; ELE_MAX]; MAX_ELE_LEVEL],
}

impl AttributeDatabase {
    /// AttributeDatabase::getAttribute (100 for out-of-range arguments, CHK_ELEMENT / CHK_ELEMENT_LEVEL).
    pub fn get_attribute(&self, level: u16, atk_ele: u16, def_ele: u16) -> i16 {
        if atk_ele as usize >= ELE_MAX || def_ele as usize >= ELE_MAX || !(1..=MAX_ELE_LEVEL as u16).contains(&level) {
            return 100;
        }
        self.attr_fix_table[level as usize - 1][atk_ele as usize][def_ele as usize]
    }
}

crate::dataset!(elemental_attribute_db, "misc_attr_fix", AttributeDatabase);

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn counts() {
        assert_eq!(status_db().len(), 1018);
        // db/status_disabled.txt: 402 rows, one per status
        assert_eq!(status_db().disabled.len(), 402);
        // db/size_fix.yml 23 weapons, db/re/size_fix.yml changes Knuckle and adds Whip
        assert_eq!(size_fix_db().len(), 24);
    }

    #[test]
    fn blessing() {
        let sc = sc_by_name("Blessing").expect("SC_BLESSING");
        let s = status_db().find(sc).expect("status");
        assert_eq!(s.icon, crate::data::constant("EFST_BLESSING").unwrap() as i32);
        assert_eq!(s.skill_id, 34); // DurationLookup AL_BLESSING
        for f in ["SCB_STR", "SCB_INT", "SCB_DEX", "SCB_HIT"] {
            assert!(s.has_calc_flag(crate::data::constant(f).unwrap() as u32), "{f}");
        }
        assert_eq!(s.calc_flag.count_ones(), 4);
        for f in ["SCF_BOSSRESIST", "SCF_TAEKWONANGEL", "SCF_REMOVEONHERMODE"] {
            assert!(s.has_flag(crate::data::constant(f).unwrap() as u32), "{f}");
        }
        assert_eq!(s.flag.count_ones(), 3);
        assert_eq!((s.state, s.opt1, s.opt2, s.opt3, s.look, s.min_duration, s.min_rate), (0, 0, 0, 0, 0, 1, 0));
        assert!(s.script.is_none() && s.fail.is_empty() && s.endonstart.is_empty());
        assert_eq!(status_db().get_icon(sc), s.icon);
        assert_eq!(status_db().get_icon(SC_MAX + 5), EFST_BLANK);
        assert_eq!(status_db().get_skill(sc), 34);
        // SCB_ALL is every flag but SCB_DYE, SCB_BATTLE also leaves out SCB_BASE
        assert_eq!(status_db().get_scb_all(), ((1u64 << SCB_MAX) - 1) & !(1 << SCB_DYE));
        assert_eq!(status_db().get_scb_battle(), status_db().get_scb_all() & !(1 << SCB_BASE));
    }

    #[test]
    fn scripts_and_disabled() {
        // a status with a Script gets every calc flag
        for s in status_db().iter().filter(|s| s.script.is_some()) {
            assert_eq!(s.calc_flag & status_db().get_scb_all(), status_db().get_scb_all());
        }
        let endure = sc_by_name("Endure").unwrap();
        assert_eq!(status_db().sc_disabled(endure), 28); // SC_ENDURE,28
        assert!(StatusDatabase::validate_status(0) && !StatusDatabase::validate_status(SC_MAX));
        assert_eq!(status_db().status_efst_get_bl_type(EFST_BLANK), BL_PC);
    }

    #[test]
    fn size_and_attribute() {
        let fist = size_fix_db().find(0).expect("W_FIST");
        assert_eq!((fist.small, fist.medium, fist.large), (100, 100, 100));
        let dagger = size_fix_db().find(1).expect("W_DAGGER");
        assert_eq!((dagger.small, dagger.medium, dagger.large), (100, 75, 50));
        // db/re/size_fix.yml: Knuckle and Whip hit large for 75
        let knuckle = crate::data::constant("W_KNUCKLE").unwrap() as i32;
        assert_eq!(size_fix_db().find(knuckle).map(|s| s.large), Some(75));
        let a = elemental_attribute_db();
        // db/re/attr_fix.yml level 1, Fire attacking: Earth 150, Water 90, Undead 125
        assert_eq!(a.get_attribute(1, 3, 2), 150);
        assert_eq!(a.get_attribute(1, 3, 1), 90);
        assert_eq!(a.get_attribute(1, 3, 9), 125);
        assert_eq!(a.get_attribute(1, 10, 0), 100); // ELE_ALL is never set
        assert_eq!(a.get_attribute(5, 0, 0), 100);
    }
}
