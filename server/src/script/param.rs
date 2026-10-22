//! pc.cpp pc_readparam / pc_setparam for the script engine's C_PARAM variables (BaseLevel, Zeny, …) and readparam.
//! Only the parameters whose data exists in this module are answered; the rest (battle status, weight, bonuses) come
//! with the systems that compute them — until then a script that needs one stops with a message instead of reading a
//! made-up value. Other ports extend the match arms here.

use crate::world::Ctx;

// enum _sp (map.hpp), the values used below.
pub const SP_BASEEXP: i64 = 1;
pub const SP_JOBEXP: i64 = 2;
pub const SP_KARMA: i64 = 3;
pub const SP_MANNER: i64 = 4;
pub const SP_STATUSPOINT: i64 = 9;
pub const SP_BASELEVEL: i64 = 11;
pub const SP_SKILLPOINT: i64 = 12;
pub const SP_STR: i64 = 13;
pub const SP_AGI: i64 = 14;
pub const SP_VIT: i64 = 15;
pub const SP_INT: i64 = 16;
pub const SP_DEX: i64 = 17;
pub const SP_LUK: i64 = 18;
pub const SP_CLASS: i64 = 19;
pub const SP_ZENY: i64 = 20;
pub const SP_SEX: i64 = 21;
pub const SP_JOBLEVEL: i64 = 55;
pub const SP_FAME: i64 = 59;
pub const SP_SITTING: i64 = 123;
pub const SP_CHARMOVE: i64 = 124;
pub const SP_CHARRENAME: i64 = 125;
pub const SP_CHARFONT: i64 = 126;
pub const SP_KILLEDGID: i64 = 118;
pub const SP_KILLERRID: i64 = 121;
pub const SP_KILLEDRID: i64 = 122;
pub const SP_COOKMASTERY: i64 = 134;
pub const SP_ACHIEVEMENT_LEVEL: i64 = 135;
pub const SP_POW: i64 = 219;
pub const SP_STA: i64 = 220;
pub const SP_WIS: i64 = 221;
pub const SP_SPL: i64 = 222;
pub const SP_CON: i64 = 223;
pub const SP_CRT: i64 = 224;
pub const SP_TRAITPOINT: i64 = 231;
/// config/const.hpp MAX_EXP (PACKETVER >= 20170830: INT64_MAX).
const MAX_EXP: u64 = i64::MAX as u64;

/// pc_readparam: Some(value), or None when the parameter's data is not in this module yet.
pub fn readparam(c: &Ctx, aid: u32, typ: i64) -> Option<i64> {
    // SP_BANK_VAULT / SP_CASHPOINTS / SP_KAFRAPOINTS (cashshop.rs)
    if let Some(v) = crate::cashshop::readparam(c, aid, typ) {
        return Some(v);
    }
    // SP_ROULETTE_BRONZE / SP_ROULETTE_SILVER / SP_ROULETTE_GOLD (roulette.rs)
    if let Some(v) = crate::roulette::readparam(c, aid, typ) {
        return Some(v);
    }
    let ch = crate::pc::character_of(c, aid)?;
    let v = match typ {
        SP_SKILLPOINT => ch.skill_point as i64,
        SP_STATUSPOINT => ch.status_point as i64,
        SP_TRAITPOINT => ch.trait_point as i64,
        SP_ZENY => ch.zeny as i64,
        SP_BASELEVEL => ch.base_level as i64,
        SP_JOBLEVEL => ch.job_level as i64,
        SP_CLASS => ch.class as i64,
        SP_SEX => ch.sex as i64,
        SP_BASEEXP => ch.base_exp.min(MAX_EXP) as i64,
        SP_JOBEXP => ch.job_exp.min(MAX_EXP) as i64,
        SP_STR => ch.str as i64,
        SP_AGI => ch.agi as i64,
        SP_VIT => ch.vit as i64,
        SP_INT => ch.int as i64,
        SP_DEX => ch.dex as i64,
        SP_LUK => ch.luk as i64,
        SP_POW => ch.pow as i64,
        SP_STA => ch.sta as i64,
        SP_WIS => ch.wis as i64,
        SP_SPL => ch.spl as i64,
        SP_CON => ch.con as i64,
        SP_CRT => ch.crt as i64,
        SP_KARMA => ch.karma as u8 as i64, // unsigned char karma (mmo.hpp:578)
        SP_MANNER => ch.manner as i64,
        SP_FAME => ch.fame as i64,
        SP_SITTING => crate::unit::get(c, aid).map(|u| (u.vd.dead_sit == 2) as i64).unwrap_or(0),
        SP_CHARMOVE => ch.moves as i64,
        SP_CHARRENAME => ch.rename as i64,
        SP_CHARFONT => ch.font as i64,
        // sd->killerrid / killedrid / killedgid (the monster system keeps them)
        SP_KILLERRID | SP_KILLEDRID | SP_KILLEDGID => return crate::mob::dead::kill_param(c, aid, typ as i32),
        SP_COOKMASTERY => crate::itemui::produce::cook_mastery(c, aid) as i64,
        SP_ACHIEVEMENT_LEVEL => crate::achievement::achievement_level_of(c, aid) as i64,
        // the calculated parameters (battle status, weight, bonuses …) come from the status system
        _ => return crate::status::param::readparam(c, aid, typ),
    };
    Some(v)
}

/// pc_setparam: the status system's port (status/param.rs); false for a parameter that cannot be set (the script
/// is ended by set_reg_num).
pub fn setparam(c: &Ctx, aid: u32, typ: i64, value: i64) -> bool {
    // SP_BANK_VAULT / SP_CASHPOINTS / SP_KAFRAPOINTS (cashshop.rs; pc_setparam's `int32 val = static_cast<int32>(val_tmp)`)
    if let Some(r) = crate::cashshop::setparam(c, aid, typ, value as u32 as i32) {
        return r;
    }
    // SP_ROULETTE_BRONZE / SP_ROULETTE_SILVER / SP_ROULETTE_GOLD (roulette.rs; the same int32 cast)
    if let Some(r) = crate::roulette::setparam(c, aid, typ, value as u32 as i32) {
        return r;
    }
    if let Some(r) = crate::mob::dead::set_kill_param(c, aid, typ as i32, value) {
        return r; // SP_KILLERRID / SP_KILLEDRID / SP_KILLEDGID
    }
    if typ == SP_COOKMASTERY {
        crate::itemui::produce::set_cook_mastery(c, aid, value as u32 as i32);
        return true;
    }
    crate::status::param::setparam(c, aid, typ, value)
}
