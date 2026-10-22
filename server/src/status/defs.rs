//! Constants of the status system that are not in the generated `consts.rs`: enums from pc.hpp, map.hpp, mob.hpp,
//! mmo.hpp, status.hpp, battle.hpp and skill.hpp (values copied from the headers of rAthena e985006, never renumbered),
//! and the status.cpp / pc.cpp macros.

#![allow(dead_code)]

// ---------------------------------------------------------------------------------------------- map.hpp / mob.hpp

/// map.hpp enum e_race.
pub const RC_NONE_: i32 = -1;
pub const RC_FORMLESS: i32 = 0;
pub const RC_UNDEAD: i32 = 1;
pub const RC_BRUTE: i32 = 2;
pub const RC_PLANT: i32 = 3;
pub const RC_INSECT: i32 = 4;
pub const RC_FISH: i32 = 5;
pub const RC_DEMON: i32 = 6;
pub const RC_DEMIHUMAN: i32 = 7;
pub const RC_ANGEL: i32 = 8;
pub const RC_DRAGON: i32 = 9;
pub const RC_PLAYER_HUMAN: i32 = 10;
pub const RC_PLAYER_DORAM: i32 = 11;
pub const RC_ALL: i32 = 12;
pub const RC_MAX: usize = 13;
/// map.hpp enum e_race2 (RC2_NONE .. RC2_MAX).
pub const RC2_NONE: i32 = 0;
pub const RC2_MAX: usize = 43;

/// map.hpp enum e_element.
pub const ELE_NONE: i32 = -1;
pub const ELE_NEUTRAL: i32 = 0;
pub const ELE_WATER: i32 = 1;
pub const ELE_EARTH: i32 = 2;
pub const ELE_FIRE: i32 = 3;
pub const ELE_WIND: i32 = 4;
pub const ELE_POISON: i32 = 5;
pub const ELE_HOLY: i32 = 6;
pub const ELE_DARK: i32 = 7;
pub const ELE_GHOST: i32 = 8;
pub const ELE_UNDEAD: i32 = 9;
pub const ELE_ALL: i32 = 10;
pub const ELE_MAX: usize = 11;

/// mob.hpp enum e_aegis_monsterclass.
pub const CLASS_NONE: i32 = -1;
pub const CLASS_NORMAL: i32 = 0;
pub const CLASS_BOSS: i32 = 1;
pub const CLASS_GUARDIAN: i32 = 2;
pub const CLASS_BATTLEFIELD: i32 = 4;
pub const CLASS_EVENT: i32 = 5;
pub const CLASS_ALL: i32 = 6;
pub const CLASS_MAX: usize = 7;

/// mob.hpp enum e_size.
pub const SZ_SMALL: i32 = 0;
pub const SZ_MEDIUM: i32 = 1;
pub const SZ_BIG: i32 = 2;
pub const SZ_ALL: i32 = 3;
pub const SZ_MAX: usize = 4;

/// map.hpp CHK_ELEMENT / CHK_RACE / CHK_CLASS, mob.hpp CHK_MOBSIZE.
pub fn chk_element(e: i32) -> bool {
    e > ELE_NONE && e < ELE_MAX as i32
}
pub fn chk_race(r: i32) -> bool {
    r > RC_NONE_ && r < RC_MAX as i32
}
pub fn chk_class(c: i32) -> bool {
    c > CLASS_NONE && c < CLASS_MAX as i32
}
pub fn chk_mobsize(s: i32) -> bool {
    (SZ_SMALL..SZ_MAX as i32).contains(&s)
}

/// map.hpp enum auto_trigger_flag.
pub const ATF_SELF: i32 = 0x01;
pub const ATF_TARGET: i32 = 0x02;
pub const ATF_SHORT: i32 = 0x04;
pub const ATF_LONG: i32 = 0x08;
pub const ATF_WEAPON: i32 = 0x10;
pub const ATF_MAGIC: i32 = 0x20;
pub const ATF_MISC: i32 = 0x40;

/// map.hpp NATURAL_HEAL_INTERVAL.
pub const NATURAL_HEAL_INTERVAL: i64 = 500;

/// map.hpp BL_* (the bits the status code tests).
pub const BL_PC: u16 = 0x001;
pub const BL_MOB: u16 = 0x002;
pub const BL_PET: u16 = 0x004;
pub const BL_HOM: u16 = 0x008;
pub const BL_MER: u16 = 0x010;
pub const BL_ITEM: u16 = 0x020;
pub const BL_SKILL: u16 = 0x040;
pub const BL_NPC: u16 = 0x080;
pub const BL_CHAT: u16 = 0x100;
pub const BL_ELEM: u16 = 0x200;
/// status.hpp BL_CONSUME / BL_REGEN.
pub const BL_CONSUME: u16 = BL_PC | BL_HOM | BL_MER | BL_ELEM;
pub const BL_REGEN: u16 = BL_PC | BL_HOM | BL_MER | BL_ELEM;

// ---------------------------------------------------------------------------------------------- mmo.hpp

/// mmo.hpp enum e_mode.
pub const MD_NONE: i32 = 0x0000000;
pub const MD_CANMOVE: i32 = 0x0000001;
pub const MD_AGGRESSIVE: i32 = 0x0000004;
pub const MD_CANATTACK: i32 = 0x0000080;
pub const MD_ANGRY: i32 = 0x0000800;
pub const MD_TARGETWEAK: i32 = 0x0004000;
pub const MD_IGNOREMELEE: i32 = 0x0010000;
pub const MD_IGNOREMAGIC: i32 = 0x0020000;
pub const MD_IGNORERANGED: i32 = 0x0040000;
pub const MD_MVP: i32 = 0x0080000;
pub const MD_IGNOREMISC: i32 = 0x0100000;
pub const MD_KNOCKBACKIMMUNE: i32 = 0x0200000;
pub const MD_DETECTOR: i32 = 0x2000000;
pub const MD_STATUSIMMUNE: i32 = 0x4000000;
pub const MD_SKILLIMMUNE: i32 = 0x8000000;
pub const MD_MASK: i32 = 0x000FFFF;

/// mmo.hpp enum equip_pos and pc.hpp's combined positions (the items system's definitions, inventory.rs).
pub use crate::inventory::{
    EQP_ACC, EQP_ACC_L, EQP_ACC_R, EQP_AMMO, EQP_ARMOR, EQP_ARMS, EQP_COSTUME_GARMENT, EQP_COSTUME_HEAD_LOW, EQP_COSTUME_HEAD_MID, EQP_COSTUME_HEAD_TOP, EQP_GARMENT,
    EQP_HAND_L, EQP_HAND_R, EQP_HEAD_LOW, EQP_HEAD_MID, EQP_HEAD_TOP, EQP_HELM, EQP_SHADOW_ACC_L, EQP_SHADOW_ACC_R, EQP_SHADOW_ARMOR, EQP_SHADOW_SHIELD,
    EQP_SHADOW_SHOES, EQP_SHADOW_WEAPON, EQP_SHIELD, EQP_SHOES, EQP_WEAPON,
};

/// mmo.hpp e_skill_flag.
pub const SKILL_FLAG_PERMANENT: u8 = 0;
pub const SKILL_FLAG_TEMPORARY: u8 = 1;
pub const SKILL_FLAG_PLAGIARIZED: u8 = 2;
pub const SKILL_FLAG_PERM_GRANTED: u8 = 3;
pub const SKILL_FLAG_TMP_COMBO: u8 = 4;
pub const SKILL_FLAG_REPLACED_LV_0: u8 = 10;

/// mmo.hpp MAX_ZENY, MAX_WEAPON_LEVEL (RENEWAL), MAX_SLOTS, MAX_ITEM_RDM_OPT; config/const.hpp MAX_EXP
/// (PACKETVER >= 20170830).
pub const MAX_ZENY: i64 = i32::MAX as i64;
pub const MAX_WEAPON_LEVEL: i32 = 5;
pub const MAX_SLOTS: usize = 4;
pub const MAX_EXP: u64 = i64::MAX as u64;
/// mmo.hpp DEFAULT_WALK_SPEED, MIN_WALK_SPEED, MAX_WALK_SPEED.
pub const DEFAULT_WALK_SPEED: i32 = 150;
pub const MIN_WALK_SPEED: i32 = 20;
pub const MAX_WALK_SPEED: i32 = 1000;
/// mmo.hpp MAX_PC_BONUS_SCRIPT.
pub const MAX_PC_BONUS_SCRIPT: usize = 50;

// ---------------------------------------------------------------------------------------------- pc.hpp

/// pc.hpp enum equip_index and pc.cpp equip_bitmask[EQI_MAX] (inventory.rs).
pub use crate::inventory::{
    EQI_ACC_L, EQI_ACC_R, EQI_AMMO, EQI_ARMOR, EQI_COSTUME_GARMENT, EQI_COSTUME_HEAD_LOW, EQI_COSTUME_HEAD_MID, EQI_COSTUME_HEAD_TOP, EQI_GARMENT, EQI_HAND_L,
    EQI_HAND_R, EQI_HEAD_LOW, EQI_HEAD_MID, EQI_HEAD_TOP, EQI_MAX, EQI_SHADOW_ACC_L, EQI_SHADOW_ACC_R, EQI_SHADOW_ARMOR, EQI_SHADOW_SHIELD, EQI_SHADOW_SHOES,
    EQI_SHADOW_WEAPON, EQI_SHOES, EQUIP_BITMASK,
};

/// pc.hpp enum weapon_type.
pub const W_FIST: i32 = 0;
pub const W_DAGGER: i32 = 1;
pub const W_1HSWORD: i32 = 2;
pub const W_2HSWORD: i32 = 3;
pub const W_1HSPEAR: i32 = 4;
pub const W_2HSPEAR: i32 = 5;
pub const W_1HAXE: i32 = 6;
pub const W_2HAXE: i32 = 7;
pub const W_MACE: i32 = 8;
pub const W_2HMACE: i32 = 9;
pub const W_STAFF: i32 = 10;
pub const W_BOW: i32 = 11;
pub const W_KNUCKLE: i32 = 12;
pub const W_MUSICAL: i32 = 13;
pub const W_WHIP: i32 = 14;
pub const W_BOOK: i32 = 15;
pub const W_KATAR: i32 = 16;
pub const W_REVOLVER: i32 = 17;
pub const W_RIFLE: i32 = 18;
pub const W_GATLING: i32 = 19;
pub const W_SHOTGUN: i32 = 20;
pub const W_GRENADE: i32 = 21;
pub const W_HUUMA: i32 = 22;
pub const W_2HSTAFF: i32 = 23;
pub const MAX_WEAPON_TYPE: i32 = 24;
pub const W_DOUBLE_DD: i32 = 25;
pub const W_DOUBLE_SS: i32 = 26;
pub const W_DOUBLE_AA: i32 = 27;
pub const W_DOUBLE_DS: i32 = 28;
pub const W_DOUBLE_DA: i32 = 29;
pub const W_DOUBLE_SA: i32 = 30;
pub const MAX_WEAPON_TYPE_ALL: i32 = 31;
pub const W_SHIELD: i32 = MAX_WEAPON_TYPE;

/// pc.hpp enum e_lr_flag.
pub const LR_FLAG_NONE: u8 = 0;
pub const LR_FLAG_WEAPON: u8 = 1;
pub const LR_FLAG_ARROW: u8 = 2;
pub const LR_FLAG_SHIELD: u8 = 3;

/// pc.hpp MAX_PC_BONUS.
pub const MAX_PC_BONUS: usize = 50;

/// pc.hpp enum e_params.
pub use crate::db::jobs::{PARAM_AGI, PARAM_CON, PARAM_CRT, PARAM_DEX, PARAM_INT, PARAM_LUK, PARAM_MAX, PARAM_POW, PARAM_SPL, PARAM_STA, PARAM_STR, PARAM_VIT, PARAM_WIS};

/// pc.hpp enum e_summoner_power_type.
pub const SUMMONER_POWER_LAND: i32 = 0;
pub const SUMMONER_POWER_LIFE: i32 = 1;
pub const SUMMONER_POWER_SEA: i32 = 2;

/// pc.hpp enum e_addskill_type.
pub const ADDSKILL_PERMANENT: i32 = 0;
pub const ADDSKILL_TEMP: i32 = 1;
pub const ADDSKILL_TEMP_ADDLEVEL: i32 = 2;
pub const ADDSKILL_PERMANENT_GRANTED: i32 = 3;

/// pc.hpp e_autospell_flags.
pub const AUTOSPELL_FORCE_SELF: u8 = 0x0;
pub const AUTOSPELL_FORCE_TARGET: u8 = 0x1;
pub const AUTOSPELL_FORCE_RANDOM_LEVEL: u8 = 0x2;
pub const AUTOSPELL_FORCE_ALL: u8 = 0x3;

/// skill.hpp MAX_SKILL_LEVEL.
pub const MAX_SKILL_LEVEL: i32 = 13;

// ---------------------------------------------------------------------------------------------- status.hpp / status.cpp

/// status.cpp enum e_regen.
pub const RGN_NONE: u8 = 0x00;
pub const RGN_HP: u8 = 0x01;
pub const RGN_SP: u8 = 0x02;
pub const RGN_SHP: u8 = 0x04;
pub const RGN_SSP: u8 = 0x08;

/// status.hpp e_status_calc_weight_opt.
pub const CALCWT_NONE: u8 = 0x0;
pub const CALCWT_ITEM: u8 = 0x1;
pub const CALCWT_MAXBONUS: u8 = 0x2;
pub const CALCWT_CARTSTATE: u8 = 0x4;

/// status.hpp e_status_change_start_flags.
pub const SCSTART_NONE: u8 = 0x0;
pub const SCSTART_NOAVOID: u8 = 0x01;
pub const SCSTART_NOTICKDEF: u8 = 0x02;
pub const SCSTART_LOADED: u8 = 0x04;
pub const SCSTART_NORATEDEF: u8 = 0x08;
pub const SCSTART_NOICON: u8 = 0x10;

/// status.hpp e_bonus_script_flags.
pub const BSF_REM_ON_DEAD: u32 = 0x001;
pub const BSF_REM_ON_DISPELL: u32 = 0x002;
pub const BSF_REM_ON_CLEARANCE: u32 = 0x004;
pub const BSF_REM_ON_LOGOUT: u32 = 0x008;
pub const BSF_REM_ON_BANISHING_BUSTER: u32 = 0x010;
pub const BSF_REM_ON_REFRESH: u32 = 0x020;
pub const BSF_REM_ON_LUXANIMA: u32 = 0x040;
pub const BSF_REM_ON_MADOGEAR: u32 = 0x080;
pub const BSF_REM_ON_DAMAGED: u32 = 0x100;
pub const BSF_PERMANENT: u32 = 0x200;
pub const BSF_FORCE_REPLACE: u32 = 0x400;
pub const BSF_FORCE_DUPLICATE: u32 = 0x800;
pub const BSF_REM_ALL: u32 = 0x0;
pub const BSF_REM_BUFF: u32 = 0x4000;
pub const BSF_REM_DEBUFF: u32 = 0x8000;

/// status.hpp e_status_bonus.
pub const STATUS_BONUS_FIX: i32 = 0;
pub const STATUS_BONUS_RATE: i32 = 1;

/// status.hpp e_sc_opt1.
pub const OPT1_NONE: i16 = 0;
pub const OPT1_STONE: i16 = 1;
pub const OPT1_FREEZE: i16 = 2;
pub const OPT1_STUN: i16 = 3;
pub const OPT1_SLEEP: i16 = 4;
pub const OPT1_STONEWAIT: i16 = 6;
pub const OPT1_BURNING: i16 = 7;
pub const OPT1_IMPRISON: i16 = 8;
/// status.hpp e_sc_opt3 (the one status_change_start sets by hand).
pub const OPT3_MOONLIT: i32 = 0x00000200;

/// status.hpp e_option.
pub const OPTION_NOTHING: i32 = 0x0;
pub const OPTION_SIGHT: i32 = 0x00000001;
pub const OPTION_HIDE: i32 = 0x00000002;
pub const OPTION_CLOAK: i32 = 0x00000004;
pub const OPTION_CART1: i32 = 0x00000008;
pub const OPTION_FALCON: i32 = 0x00000010;
pub const OPTION_RIDING: i32 = 0x00000020;
pub const OPTION_INVISIBLE: i32 = 0x00000040;
pub const OPTION_CART2: i32 = 0x00000080;
pub const OPTION_CART3: i32 = 0x00000100;
pub const OPTION_CART4: i32 = 0x00000200;
pub const OPTION_CART5: i32 = 0x00000400;
pub const OPTION_ORCISH: i32 = 0x00000800;
pub const OPTION_WEDDING: i32 = 0x00001000;
pub const OPTION_RUWACH: i32 = 0x00002000;
pub const OPTION_CHASEWALK: i32 = 0x00004000;
pub const OPTION_FLYING: i32 = 0x00008000;
pub const OPTION_XMAS: i32 = 0x00010000;
pub const OPTION_TRANSFORM: i32 = 0x00020000;
pub const OPTION_SUMMER: i32 = 0x00040000;
pub const OPTION_DRAGON1: i32 = 0x00080000;
pub const OPTION_WUG: i32 = 0x00100000;
pub const OPTION_WUGRIDER: i32 = 0x00200000;
pub const OPTION_MADOGEAR: i32 = 0x00400000;
pub const OPTION_DRAGON2: i32 = 0x00800000;
pub const OPTION_DRAGON3: i32 = 0x01000000;
pub const OPTION_DRAGON4: i32 = 0x02000000;
pub const OPTION_DRAGON5: i32 = 0x04000000;
pub const OPTION_HANBOK: i32 = 0x08000000;
pub const OPTION_OKTOBERFEST: i32 = 0x10000000;
pub const OPTION_SUMMER2: i32 = 0x20000000;
pub const OPTION_DRAGON: i32 = OPTION_DRAGON1 | OPTION_DRAGON2 | OPTION_DRAGON3 | OPTION_DRAGON4 | OPTION_DRAGON5;
pub const OPTION_COSTUME: i32 = OPTION_WEDDING | OPTION_XMAS | OPTION_SUMMER | OPTION_HANBOK | OPTION_OKTOBERFEST | OPTION_SUMMER2;

/// status.hpp e_joint_break.
pub const BREAK_ANKLE: i32 = 0x01;
pub const BREAK_WRIST: i32 = 0x02;
pub const BREAK_KNEE: i32 = 0x04;
pub const BREAK_SHOULDER: i32 = 0x08;
pub const BREAK_WAIST: i32 = 0x10;
pub const BREAK_NECK: i32 = 0x20;

/// status.hpp MIN_ASPD, MAX_ASPD_NOPC, AMOTION_DIVIDER_PC / NOPC, AMOTION_ZERO_ASPD, AMOTION_INTERVAL, MAX_REFINE (RENEWAL).
pub const MIN_ASPD: i32 = 8000;
pub const MAX_ASPD_NOPC: i32 = 100;
pub const AMOTION_DIVIDER_PC: i32 = 2;
pub const AMOTION_DIVIDER_NOPC: i32 = 1;
pub const AMOTION_ZERO_ASPD: i32 = 2000;
pub const AMOTION_INTERVAL: i32 = 10;
pub const MAX_REFINE: u8 = 20;
/// config/const.hpp DEFTYPE_MIN / DEFTYPE_MAX (RENEWAL: int16).
pub const DEFTYPE_MIN: i32 = i16::MIN as i32;
pub const DEFTYPE_MAX: i32 = i16::MAX as i32;

// ---------------------------------------------------------------------------------------------- battle.hpp / skill.hpp

/// battle.hpp e_battle_flag.
pub const BF_WEAPON: i32 = 0x0001;
pub const BF_MAGIC: i32 = 0x0002;
pub const BF_MISC: i32 = 0x0004;
pub const BF_SHORT: i32 = 0x0010;
pub const BF_LONG: i32 = 0x0040;
pub const BF_SKILL: i32 = 0x0100;
pub const BF_NORMAL: i32 = 0x0200;
pub const BF_WEAPONMASK: i32 = BF_WEAPON | BF_MAGIC | BF_MISC;
pub const BF_RANGEMASK: i32 = BF_SHORT | BF_LONG;
pub const BF_SKILLMASK: i32 = BF_SKILL | BF_NORMAL;
/// battle.hpp e_battle_check_target (the two status.cpp compares with).
pub const BCT_SELF: i32 = 0x010000;
pub const BCT_ENEMY: i32 = 0x020000;

/// skill.hpp e_nibelungen_status (RINGNBL_*).
pub const RINGNBL_ASPDRATE: i32 = 1;
pub const RINGNBL_ATKRATE: i32 = 2;
pub const RINGNBL_MATKRATE: i32 = 3;
pub const RINGNBL_HPRATE: i32 = 4;
pub const RINGNBL_SPRATE: i32 = 5;
pub const RINGNBL_ALLSTAT: i32 = 6;
pub const RINGNBL_HIT: i32 = 7;
pub const RINGNBL_FLEE: i32 = 8;
pub const RINGNBL_SPCONSUM: i32 = 9;
pub const RINGNBL_HPREGEN: i32 = 10;
pub const RINGNBL_SPREGEN: i32 = 11;

/// clif.hpp e_notify_effect (clif_misceffect).
pub const NOTIFYEFFECT_BASE_LEVEL_UP: i32 = 0;
pub const NOTIFYEFFECT_JOB_LEVEL_UP: i32 = 1;

// ---------------------------------------------------------------------------------------------- helpers

/// cbasetypes.hpp cap_value.
pub fn cap_value<T: PartialOrd>(a: T, min: T, max: T) -> T {
    if a >= max {
        max
    } else if a <= min {
        min
    } else {
        a
    }
}

/// `(uint16)cap_value(x, 0, USHRT_MAX)` stored into an int16 field (status_data's stats are int16).
pub fn stat_u16_to_i16(v: i32) -> i16 {
    cap_value(v, 0, u16::MAX as i32) as u16 as i16
}

/// utilities.hpp util::safe_addition_cap: a + b, capped at `cap` when the sum would pass it (either direction for the
/// int types rAthena uses it with).
pub fn safe_addition_cap(a: i64, b: i64, cap: i64) -> i64 {
    let r = a.saturating_add(b);
    if r > cap {
        cap
    } else {
        r
    }
}

/// map.hpp apply_rate: a * rate / 100 (with the int64 intermediate rAthena uses).
pub fn apply_rate(a: u32, rate: i32) -> u32 {
    ((a as i64) * rate as i64 / 100) as u32
}

/// status.hpp e_status_calc_opt.
pub const SCO_NONE: u8 = 0x0;
pub const SCO_FIRST: u8 = 0x1;
pub const SCO_FORCE: u8 = 0x2;
