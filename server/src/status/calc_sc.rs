//! status.cpp's per-stat status change modifiers (status_calc_str … status_calc_mode, status_calc_pseudobuff_matk,
//! status_calc_consumablematk, status_calc_aspd, status_get_hpbonus / spbonus / apbonus and their equip / item
//! variants, status_calc_maxhp / maxsp / maxap for non-players, status_calc_regen_rate): every `sc->getSCE(SC_X)`
//! line of rAthena e985006, RENEWAL / RENEWAL_ASPD branches.

#![allow(clippy::needless_return, clippy::collapsible_else_if, clippy::collapsible_if)]

use super::consts::*;
use super::defs::*;
use super::sc::Sc;
use crate::db::skills::*;
use crate::world::Ctx;

/// What the per-stat functions read about the unit (rAthena passes the block_list and casts it).
pub struct Who<'a> {
    pub c: &'a Ctx<'a>,
    /// block_list::type.
    pub kind: u16,
    pub id: u32,
    pub map: u16,
    /// The player's side (BL_PC only).
    pub pc: Option<PcView>,
}

/// The map_session_data fields the per-stat functions read.
#[derive(Clone, Debug, Default)]
pub struct PcView {
    pub class_: u64,
    pub sex: u8,
    /// sd->status.weapon.
    pub weapon: i32,
    pub speed_rate: i32,
    pub speed_add_rate: i32,
    pub permanent_speed: bool,
    /// sd->ud.skilltimer != INVALID_TIMER, ud.skill_id, ud.skill_lv.
    pub casting: bool,
    pub skill_id: u16,
    pub skill_lv: u16,
    /// sd->sc.option.
    pub option: i32,
}

impl Who<'_> {
    /// pc_checkskill (0 for non-players).
    pub fn checkskill(&self, skill_id: u16) -> i32 {
        if self.kind != BL_PC {
            return 0;
        }
        crate::skill::checkskill(self.c, self.id, skill_id) as i32
    }
}

fn cap_u16(v: i32) -> u16 {
    cap_value(v, 0, u16::MAX as i32) as u16
}
fn cap16(v: i32, lo: i32, hi: i32) -> i16 {
    cap_value(v, lo, hi) as i16
}

/// status.cpp status_calc_str.
pub fn status_calc_str(w: &Who, sc: &Sc, mut str: i32) -> u16 {
    let _ = w;
    if sc.empty() {
        return cap_u16(str);
    }
    if sc.has(SC_HARMONIZE) {
        str -= sc.v2(SC_HARMONIZE);
        return cap_u16(str);
    }
    if sc.has(SC_INCALLSTATUS) { str += sc.v1(SC_INCALLSTATUS); }
    if sc.has(SC_CHASEWALK2) { str += sc.v1(SC_CHASEWALK2); }
    if sc.has(SC_BATTLEORDERS) { str += 5; }
    if sc.has(SC_LEADERSHIP) { str += sc.v1(SC_LEADERSHIP); }
    if sc.has(SC_LOUD) { str += 4; }
    if sc.has(SC_TRUESIGHT) { str += 5; }
    if sc.has(SC_SPURT) { str += 10; }
    if sc.has(SC_NEN) { str += sc.v1(SC_NEN); }
    if sc.has(SC_BLESSING) {
        if sc.v2(SC_BLESSING) != 0 { str += sc.v2(SC_BLESSING); } else { str -= str / 2; }
    }
    if sc.has(SC_MARIONETTE) { str -= (sc.v3(SC_MARIONETTE) >> 16) & 0xFF; }
    if sc.has(SC_MARIONETTE2) { str += (sc.v3(SC_MARIONETTE2) >> 16) & 0xFF; }
    if sc.has(SC_SPIRIT) && sc.v2(SC_SPIRIT) == SL_HIGH as i32 { str += (sc.v3(SC_SPIRIT) >> 16) & 0xFF; }
    if sc.has(SC_GIANTGROWTH) { str += sc.v2(SC_GIANTGROWTH); }
    if sc.has(SC_BEYONDOFWARCRY) { str -= sc.v2(SC_BEYONDOFWARCRY); }
    if sc.has(SC_INSPIRATION) { str += sc.v3(SC_INSPIRATION); }
    if sc.has(SC_STOMACHACHE) { str -= sc.v1(SC_STOMACHACHE); }
    if sc.has(SC_KYOUGAKU) { str -= sc.v2(SC_KYOUGAKU); }
    if sc.has(SC_SWORDCLAN) { str += 1; }
    if sc.has(SC_JUMPINGCLAN) { str += 1; }
    if sc.has(SC_FULL_THROTTLE) { str += str * sc.v3(SC_FULL_THROTTLE) / 100; }
    if sc.has(SC_CHEERUP) { str += 3; }
    if sc.has(SC_NIBELUNGEN) && sc.v2(SC_NIBELUNGEN) == RINGNBL_ALLSTAT { str += 15; }
    if sc.has(SC_UNIVERSESTANCE) { str += sc.v2(SC_UNIVERSESTANCE); }
    if sc.has(SC_ALL_STAT_DOWN) { str -= sc.v2(SC_ALL_STAT_DOWN); }
    cap_u16(str)
}

/// status.cpp status_calc_agi.
pub fn status_calc_agi(w: &Who, sc: &Sc, mut agi: i32) -> u16 {
    let _ = w;
    if sc.empty() {
        return cap_u16(agi);
    }
    if sc.has(SC_HARMONIZE) {
        agi -= sc.v2(SC_HARMONIZE);
        return cap_u16(agi);
    }
    if sc.has(SC_CONCENTRATE) && !sc.has(SC_QUAGMIRE) { agi += (agi - sc.v3(SC_CONCENTRATE)) * sc.v2(SC_CONCENTRATE) / 100; }
    if sc.has(SC_INCALLSTATUS) { agi += sc.v1(SC_INCALLSTATUS); }
    if sc.has(SC_SOULCOLD) { agi += sc.v1(SC_SOULCOLD); }
    if sc.has(SC_TRUESIGHT) { agi += 5; }
    if sc.has(SC_INCREASEAGI) { agi += sc.v2(SC_INCREASEAGI); }
    if sc.has(SC_INCREASING) { agi += 4; }
    if sc.has(SC_DECREASEAGI) { agi -= sc.v2(SC_DECREASEAGI); }
    if sc.has(SC_QUAGMIRE) { agi -= sc.v2(SC_QUAGMIRE); }
    if sc.has(SC_SUITON) && sc.v3(SC_SUITON) != 0 { agi -= sc.v2(SC_SUITON); }
    if sc.has(SC_MARIONETTE) { agi -= (sc.v3(SC_MARIONETTE) >> 8) & 0xFF; }
    if sc.has(SC_MARIONETTE2) { agi += (sc.v3(SC_MARIONETTE2) >> 8) & 0xFF; }
    if sc.has(SC_SPIRIT) && sc.v2(SC_SPIRIT) == SL_HIGH as i32 { agi += (sc.v3(SC_SPIRIT) >> 8) & 0xFF; }
    if sc.has(SC_ADORAMUS) { agi -= sc.v2(SC_ADORAMUS); }
    if sc.has(SC_MARSHOFABYSS) { agi -= agi * sc.v2(SC_MARSHOFABYSS) / 100; }
    if sc.has(SC_INSPIRATION) { agi += sc.v3(SC_INSPIRATION); }
    if sc.has(SC_STOMACHACHE) { agi -= sc.v1(SC_STOMACHACHE); }
    if sc.has(SC_KYOUGAKU) { agi -= sc.v2(SC_KYOUGAKU); }
    if sc.has(SC_CROSSBOWCLAN) { agi += 1; }
    if sc.has(SC_JUMPINGCLAN) { agi += 1; }
    if sc.has(SC_FULL_THROTTLE) { agi += agi * sc.v3(SC_FULL_THROTTLE) / 100; }
    if sc.has(SC_ARCLOUSEDASH) { agi += sc.v2(SC_ARCLOUSEDASH); }
    if sc.has(SC_CHEERUP) { agi += 3; }
    if sc.has(SC_NIBELUNGEN) && sc.v2(SC_NIBELUNGEN) == RINGNBL_ALLSTAT { agi += 15; }
    if sc.has(SC_UNIVERSESTANCE) { agi += sc.v2(SC_UNIVERSESTANCE); }
    if sc.has(SC_ALL_STAT_DOWN) { agi -= sc.v2(SC_ALL_STAT_DOWN); }
    cap_u16(agi)
}

/// status.cpp status_calc_vit.
pub fn status_calc_vit(w: &Who, sc: &Sc, mut vit: i32) -> u16 {
    if sc.empty() {
        return cap_u16(vit);
    }
    if sc.has(SC_HARMONIZE) {
        vit -= sc.v2(SC_HARMONIZE);
        return cap_u16(vit);
    }
    if sc.has(SC_INCALLSTATUS) { vit += sc.v1(SC_INCALLSTATUS); }
    if sc.has(SC_CHANGE) { vit += sc.v2(SC_CHANGE); }
    if sc.has(SC_GLORYWOUNDS) { vit += sc.v1(SC_GLORYWOUNDS); }
    if sc.has(SC_TRUESIGHT) { vit += 5; }
    if sc.has(SC_MARIONETTE) { vit -= sc.v3(SC_MARIONETTE) & 0xFF; }
    if sc.has(SC_MARIONETTE2) { vit += sc.v3(SC_MARIONETTE2) & 0xFF; }
    if sc.has(SC_SPIRIT) && sc.v2(SC_SPIRIT) == SL_HIGH as i32 { vit += sc.v3(SC_SPIRIT) & 0xFF; }
    if sc.has(SC_INSPIRATION) { vit += sc.v3(SC_INSPIRATION); }
    if sc.has(SC_STOMACHACHE) { vit -= sc.v1(SC_STOMACHACHE); }
    if sc.has(SC_KYOUGAKU) { vit -= sc.v2(SC_KYOUGAKU); }
    if sc.has(SC_SWORDCLAN) { vit += 1; }
    if sc.has(SC_JUMPINGCLAN) { vit += 1; }
    if sc.has(SC_STRIPARMOR) && w.kind != BL_PC { vit -= vit * sc.v2(SC_STRIPARMOR) / 100; }
    if sc.has(SC_FULL_THROTTLE) { vit += vit * sc.v3(SC_FULL_THROTTLE) / 100; }
    if w.kind == BL_PC && sc.has(SC_DEFENCE) { vit += sc.v2(SC_DEFENCE); }
    if sc.has(SC_CHEERUP) { vit += 3; }
    if sc.has(SC_NIBELUNGEN) && sc.v2(SC_NIBELUNGEN) == RINGNBL_ALLSTAT { vit += 15; }
    if sc.has(SC_UNIVERSESTANCE) { vit += sc.v2(SC_UNIVERSESTANCE); }
    if sc.has(SC_ALL_STAT_DOWN) { vit -= sc.v2(SC_ALL_STAT_DOWN); }
    cap_u16(vit)
}

/// status.cpp status_calc_int.
pub fn status_calc_int(w: &Who, sc: &Sc, mut int_: i32) -> u16 {
    if sc.empty() {
        return cap_u16(int_);
    }
    if sc.has(SC_HARMONIZE) {
        int_ -= sc.v2(SC_HARMONIZE);
        return cap_u16(int_);
    }
    if sc.has(SC_INCALLSTATUS) { int_ += sc.v1(SC_INCALLSTATUS); }
    if sc.has(SC_CHANGE) { int_ += sc.v3(SC_CHANGE); }
    if sc.has(SC_BATTLEORDERS) { int_ += 5; }
    if sc.has(SC_TRUESIGHT) { int_ += 5; }
    if sc.has(SC_BLESSING) {
        if sc.v2(SC_BLESSING) != 0 { int_ += sc.v2(SC_BLESSING); } else { int_ -= int_ / 2; }
    }
    if sc.has(SC_NEN) { int_ += sc.v1(SC_NEN); }
    if sc.has(SC_MARIONETTE) { int_ -= (sc.v4(SC_MARIONETTE) >> 16) & 0xFF; }
    if sc.has(SC_MARIONETTE2) { int_ += (sc.v4(SC_MARIONETTE2) >> 16) & 0xFF; }
    if sc.has(SC_SPIRIT) && sc.v2(SC_SPIRIT) == SL_HIGH as i32 { int_ += (sc.v4(SC_SPIRIT) >> 16) & 0xFF; }
    if sc.has(SC_INSPIRATION) { int_ += sc.v3(SC_INSPIRATION); }
    if sc.has(SC_MELODYOFSINK) { int_ -= sc.v2(SC_MELODYOFSINK); }
    if sc.has(SC_MANDRAGORA) { int_ -= 4 * sc.v1(SC_MANDRAGORA); }
    if sc.has(SC_STOMACHACHE) { int_ -= sc.v1(SC_STOMACHACHE); }
    if sc.has(SC_KYOUGAKU) { int_ -= sc.v2(SC_KYOUGAKU); }
    if sc.has(SC_ARCWANDCLAN) { int_ += 1; }
    if sc.has(SC_GOLDENMACECLAN) { int_ += 1; }
    if sc.has(SC_JUMPINGCLAN) { int_ += 1; }
    if sc.has(SC_FULL_THROTTLE) { int_ += int_ * sc.v3(SC_FULL_THROTTLE) / 100; }
    if sc.has(SC_CHEERUP) { int_ += 3; }
    if sc.has(SC_UNIVERSESTANCE) { int_ += sc.v2(SC_UNIVERSESTANCE); }
    if w.kind != BL_PC {
        if sc.has(SC_STRIPHELM) { int_ -= int_ * sc.v2(SC_STRIPHELM) / 100; }
        if sc.has(SC__STRIPACCESSORY) { int_ -= int_ * sc.v2(SC__STRIPACCESSORY) / 100; }
    }
    if sc.has(SC_NIBELUNGEN) && sc.v2(SC_NIBELUNGEN) == RINGNBL_ALLSTAT { int_ += 15; }
    if sc.has(SC_ALL_STAT_DOWN) { int_ -= sc.v2(SC_ALL_STAT_DOWN); }
    cap_u16(int_)
}

/// status.cpp status_calc_dex.
pub fn status_calc_dex(w: &Who, sc: &Sc, mut dex: i32) -> u16 {
    if sc.empty() {
        return cap_u16(dex);
    }
    if sc.has(SC_HARMONIZE) {
        dex -= sc.v2(SC_HARMONIZE);
        return cap_u16(dex);
    }
    if sc.has(SC_CONCENTRATE) && !sc.has(SC_QUAGMIRE) { dex += (dex - sc.v4(SC_CONCENTRATE)) * sc.v2(SC_CONCENTRATE) / 100; }
    if sc.has(SC_INCALLSTATUS) { dex += sc.v1(SC_INCALLSTATUS); }
    if sc.has(SC_BATTLEORDERS) { dex += 5; }
    if sc.has(SC_HAWKEYES) { dex += sc.v1(SC_HAWKEYES); }
    if sc.has(SC_TRUESIGHT) { dex += 5; }
    if sc.has(SC_QUAGMIRE) { dex -= sc.v2(SC_QUAGMIRE); }
    if sc.has(SC_BLESSING) {
        if sc.v2(SC_BLESSING) != 0 { dex += sc.v2(SC_BLESSING); } else { dex -= dex / 2; }
    }
    if sc.has(SC_INCREASING) { dex += 4; }
    if sc.has(SC_MARIONETTE) { dex -= (sc.v4(SC_MARIONETTE) >> 8) & 0xFF; }
    if sc.has(SC_MARIONETTE2) { dex += (sc.v4(SC_MARIONETTE2) >> 8) & 0xFF; }
    if sc.has(SC_SPIRIT) && sc.v2(SC_SPIRIT) == SL_HIGH as i32 { dex += (sc.v4(SC_SPIRIT) >> 8) & 0xFF; }
    if sc.has(SC_INSPIRATION) { dex += sc.v3(SC_INSPIRATION); }
    if sc.has(SC_STOMACHACHE) { dex -= sc.v1(SC_STOMACHACHE); }
    if sc.has(SC_KYOUGAKU) { dex -= sc.v2(SC_KYOUGAKU); }
    if sc.has(SC_ARCWANDCLAN) { dex += 1; }
    if sc.has(SC_CROSSBOWCLAN) { dex += 1; }
    if sc.has(SC_JUMPINGCLAN) { dex += 1; }
    if sc.has(SC__STRIPACCESSORY) && w.kind != BL_PC { dex -= dex * sc.v2(SC__STRIPACCESSORY) / 100; }
    if sc.has(SC_MARSHOFABYSS) { dex -= dex * sc.v2(SC_MARSHOFABYSS) / 100; }
    if sc.has(SC_FULL_THROTTLE) { dex += dex * sc.v3(SC_FULL_THROTTLE) / 100; }
    if sc.has(SC_CHEERUP) { dex += 3; }
    if sc.has(SC_NIBELUNGEN) && sc.v2(SC_NIBELUNGEN) == RINGNBL_ALLSTAT { dex += 15; }
    if sc.has(SC_UNIVERSESTANCE) { dex += sc.v2(SC_UNIVERSESTANCE); }
    if sc.has(SC_ALL_STAT_DOWN) { dex -= sc.v2(SC_ALL_STAT_DOWN); }
    cap_u16(dex)
}

/// status.cpp status_calc_luk.
pub fn status_calc_luk(w: &Who, sc: &Sc, mut luk: i32) -> u16 {
    if sc.empty() {
        return cap_u16(luk);
    }
    if sc.has(SC_HARMONIZE) {
        luk -= sc.v2(SC_HARMONIZE);
        return cap_u16(luk);
    }
    if sc.has(SC_CURSE) {
        return 0;
    }
    if sc.has(SC_INCALLSTATUS) { luk += sc.v1(SC_INCALLSTATUS); }
    if sc.has(SC_TRUESIGHT) { luk += 5; }
    if sc.has(SC_GLORIA) { luk += 30; }
    if sc.has(SC_MARIONETTE) { luk -= sc.v4(SC_MARIONETTE) & 0xFF; }
    if sc.has(SC_MARIONETTE2) { luk += sc.v4(SC_MARIONETTE2) & 0xFF; }
    if sc.has(SC_SPIRIT) && sc.v2(SC_SPIRIT) == SL_HIGH as i32 { luk += sc.v4(SC_SPIRIT) & 0xFF; }
    if sc.has(SC_INSPIRATION) { luk += sc.v3(SC_INSPIRATION); }
    if sc.has(SC_STOMACHACHE) { luk -= sc.v1(SC_STOMACHACHE); }
    if sc.has(SC_KYOUGAKU) { luk -= sc.v2(SC_KYOUGAKU); }
    if sc.has(SC__STRIPACCESSORY) && w.kind != BL_PC { luk -= luk * sc.v2(SC__STRIPACCESSORY) / 100; }
    if sc.has(SC_BANANA_BOMB) { luk -= 75; }
    if sc.has(SC_GOLDENMACECLAN) { luk += 1; }
    if sc.has(SC_JUMPINGCLAN) { luk += 1; }
    if sc.has(SC_FULL_THROTTLE) { luk += luk * sc.v3(SC_FULL_THROTTLE) / 100; }
    if sc.has(SC_CHEERUP) { luk += 3; }
    if sc.has(SC_NIBELUNGEN) && sc.v2(SC_NIBELUNGEN) == RINGNBL_ALLSTAT { luk += 15; }
    if sc.has(SC_UNIVERSESTANCE) { luk += sc.v2(SC_UNIVERSESTANCE); }
    if sc.has(SC_ALL_STAT_DOWN) { luk -= sc.v2(SC_ALL_STAT_DOWN); }
    cap_u16(luk)
}

/// status.cpp status_calc_pow.
pub fn status_calc_pow(_w: &Who, sc: &Sc, mut pow: i32) -> u16 {
    if sc.empty() {
        return cap_u16(pow);
    }
    if sc.has(SC_BENEDICTUM) { pow += sc.v2(SC_BENEDICTUM); }
    if sc.has(SC_MARINE_FESTIVAL) { pow += sc.v2(SC_MARINE_FESTIVAL); }
    cap_u16(pow)
}

/// status.cpp status_calc_sta.
pub fn status_calc_sta(_w: &Who, sc: &Sc, mut sta: i32) -> u16 {
    if sc.empty() {
        return cap_u16(sta);
    }
    if sc.has(SC_RELIGIO) { sta += sc.v2(SC_RELIGIO); }
    if sc.has(SC_SANDY_FESTIVAL) { sta += sc.v2(SC_SANDY_FESTIVAL); }
    cap_u16(sta)
}

/// status.cpp status_calc_wis.
pub fn status_calc_wis(_w: &Who, sc: &Sc, mut wis: i32) -> u16 {
    if sc.empty() {
        return cap_u16(wis);
    }
    if sc.has(SC_RELIGIO) { wis += sc.v2(SC_RELIGIO); }
    if sc.has(SC_SANDY_FESTIVAL) { wis += sc.v2(SC_SANDY_FESTIVAL); }
    cap_u16(wis)
}

/// status.cpp status_calc_spl.
pub fn status_calc_spl(_w: &Who, sc: &Sc, mut spl: i32) -> u16 {
    if sc.empty() {
        return cap_u16(spl);
    }
    if sc.has(SC_RELIGIO) { spl += sc.v2(SC_RELIGIO); }
    if sc.has(SC_SANDY_FESTIVAL) { spl += sc.v2(SC_SANDY_FESTIVAL); }
    cap_u16(spl)
}

/// status.cpp status_calc_con.
pub fn status_calc_con(_w: &Who, sc: &Sc, mut con: i32) -> u16 {
    if sc.empty() {
        return cap_u16(con);
    }
    if sc.has(SC_BENEDICTUM) { con += sc.v2(SC_BENEDICTUM); }
    if sc.has(SC_MARINE_FESTIVAL) { con += sc.v2(SC_MARINE_FESTIVAL); }
    cap_u16(con)
}

/// status.cpp status_calc_crt.
pub fn status_calc_crt(_w: &Who, sc: &Sc, mut crt: i32) -> u16 {
    if sc.empty() {
        return cap_u16(crt);
    }
    if sc.has(SC_BENEDICTUM) { crt += sc.v2(SC_BENEDICTUM); }
    if sc.has(SC_MARINE_FESTIVAL) { crt += sc.v2(SC_MARINE_FESTIVAL); }
    cap_u16(crt)
}

/// status.cpp status_calc_batk (RENEWAL).
pub fn status_calc_batk(w: &Who, sc: &Sc, mut batk: i32) -> i32 {
    if sc.empty() {
        return batk;
    }
    if sc.has(SC_VOLCANO) { batk += sc.v2(SC_VOLCANO); }
    if sc.has(SC_ASH) { batk -= batk * sc.v4(SC_ASH) / 100; }
    if w.kind == BL_HOM && sc.has(SC_PYROCLASTIC) { batk += sc.v2(SC_PYROCLASTIC); }
    if sc.has(SC_ANGRIFFS_MODUS) { batk += sc.v2(SC_ANGRIFFS_MODUS); }
    if sc.has(SC__ENERVATION) { batk -= batk * sc.v2(SC__ENERVATION) / 100; }
    if sc.has(SC_ZANGETSU) { batk += sc.v2(SC_ZANGETSU); }
    if sc.has(SC_LOUD) { batk += 30; }
    if sc.has(SC_NIBELUNGEN) && sc.v2(SC_NIBELUNGEN) == RINGNBL_ATKRATE { batk += batk * 20 / 100; }
    if sc.has(SC_SUNSTANCE) { batk += batk * sc.v2(SC_SUNSTANCE) / 100; }
    if sc.has(SC_INTENSIVE_AIM) { batk += 150; }
    batk
}

/// status.cpp status_calc_watk (RENEWAL).
pub fn status_calc_watk(w: &Who, sc: &Sc, mut watk: i32) -> u16 {
    if sc.empty() {
        return cap_u16(watk);
    }
    if sc.has(SC_IMPOSITIO) { watk += sc.v2(SC_IMPOSITIO); }
    if sc.has(SC_WATKFOOD) { watk += sc.v1(SC_WATKFOOD); }
    if sc.has(SC_VOLCANO) && w.kind == BL_MOB { watk += sc.v2(SC_VOLCANO); }
    if sc.has(SC_MERC_ATKUP) { watk += sc.v2(SC_MERC_ATKUP); }
    if sc.has(SC_WATER_BARRIER) { watk -= sc.v2(SC_WATER_BARRIER); }
    if sc.has(SC_FIGHTINGSPIRIT) { watk += sc.v1(SC_FIGHTINGSPIRIT); }
    if sc.has(SC_SHIELDSPELL_ATK) { watk += sc.v2(SC_SHIELDSPELL_ATK); }
    if sc.has(SC_INSPIRATION) { watk += sc.v2(SC_INSPIRATION); }
    if sc.has(SC_GT_CHANGE) { watk += sc.v2(SC_GT_CHANGE); }
    if sc.has(SC__ENERVATION) { watk -= watk * sc.v2(SC__ENERVATION) / 100; }
    if sc.has(SC_STRIKING) { watk += sc.v2(SC_STRIKING); }
    if sc.has(SC_RUSHWINDMILL) { watk += sc.v3(SC_RUSHWINDMILL); }
    if sc.has(SC_FIRE_INSIGNIA) && sc.v1(SC_FIRE_INSIGNIA) == 2 { watk += 50; }
    if (sc.has(SC_FIRE_INSIGNIA) && sc.v1(SC_FIRE_INSIGNIA) == 2)
        || (sc.has(SC_WATER_INSIGNIA) && sc.v1(SC_WATER_INSIGNIA) == 2)
        || (sc.has(SC_WIND_INSIGNIA) && sc.v1(SC_WIND_INSIGNIA) == 2)
        || (sc.has(SC_EARTH_INSIGNIA) && sc.v1(SC_EARTH_INSIGNIA) == 2)
    {
        watk += watk * 10 / 100;
    }
    if sc.has(SC_PYROTECHNIC_OPTION) { watk += sc.v2(SC_PYROTECHNIC_OPTION); }
    if sc.has(SC_HEATER_OPTION) { watk += sc.v2(SC_HEATER_OPTION); }
    if sc.has(SC_TROPIC_OPTION) { watk += sc.v2(SC_TROPIC_OPTION); }
    if sc.has(SC_TIDAL_WEAPON) { watk += watk * sc.v2(SC_TIDAL_WEAPON) / 100; }
    if w.kind == BL_PC && sc.has(SC_PYROCLASTIC) { watk += sc.v2(SC_PYROCLASTIC); }
    if sc.has(SC_ANGRIFFS_MODUS) { watk += watk * sc.v2(SC_ANGRIFFS_MODUS) / 100; }
    if sc.has(SC_ODINS_POWER) { watk += 40 + 30 * sc.v1(SC_ODINS_POWER); }
    if sc.has(SC_FLASHCOMBO) { watk += sc.v2(SC_FLASHCOMBO); }
    if sc.has(SC_CATNIPPOWDER) { watk += watk * sc.v2(SC_CATNIPPOWDER) / 100; }
    if sc.has(SC_CHATTERING) { watk += sc.v2(SC_CHATTERING); }
    if sc.has(SC_SUNSTANCE) { watk += watk * sc.v2(SC_SUNSTANCE) / 100; }
    if sc.has(SC_SOULFALCON) { watk += sc.v2(SC_SOULFALCON); }
    if sc.has(SC_POWERFUL_FAITH) { watk += sc.v2(SC_POWERFUL_FAITH); }
    if sc.has(SC_GUARD_STANCE) { watk -= sc.v3(SC_GUARD_STANCE); }
    cap_u16(watk)
}

/// status.cpp status_calc_pseudobuff_matk (RENEWAL). `w` is the player (sd may be nullptr in rAthena for non-players:
/// pc_checkskill then returns 0).
pub fn status_calc_pseudobuff_matk(w: &Who, sc: &Sc, mut matk: i32) -> u16 {
    // Flat MATK bonus from skills without sc
    let skill_lv = w.checkskill(NV_TRANSCENDENCE);
    if skill_lv > 0 {
        matk += 15 * skill_lv + if skill_lv > 4 { 25 } else { 0 };
    }
    if sc.empty() {
        return cap_u16(matk);
    }
    // Flat MATK bonus from skills with sc (renewal-only ones first)
    if let Some(e) = sc.get(SC_IMPOSITIO) { matk += e.val2; }
    if let Some(e) = sc.get(SC_VOLCANO) { matk += e.val2; }
    if let Some(e) = sc.get(SC_DORAM_MATK) { matk += e.val1; }
    if let Some(e) = sc.get(SC_AQUAPLAY_OPTION) { matk += e.val2; }
    if let Some(e) = sc.get(SC_CHILLY_AIR_OPTION) { matk += e.val2; }
    if let Some(e) = sc.get(SC_COOLER_OPTION) { matk += e.val2; }
    if let Some(e) = sc.get(SC_SOULFAIRY) { matk += e.val2; }
    if let Some(e) = sc.get(SC_INSPIRATION) { matk += e.val2; }
    if let Some(e) = sc.get(SC_SHIELDSPELL_ATK) { matk += e.val2; }
    if let Some(e) = sc.get(SC_ZANGETSU) { matk += e.val3; }
    if let Some(e) = sc.get(SC_MOONLITSERENADE) { matk += e.val3; }
    if let Some(e) = sc.get(SC__AUTOSHADOWSPELL) { matk += e.val4; }
    if let Some(e) = sc.get(SC_ODINS_POWER) { matk += 40 + 30 * e.val1; }
    if let Some(e) = sc.get(SC_IZAYOI) { matk += 25 * e.val1; }
    if let Some(e) = sc.get(SC_FIRE_INSIGNIA) {
        if e.val1 == 3 { matk += 50; }
    }
    if sc.has(SC_CLIMAX_DES_HU) { matk += 100; }
    cap_u16(matk)
}

/// status.cpp status_calc_consumablematk.
pub fn status_calc_consumablematk(sc: &Sc, matk: i32) -> u16 {
    let _ = sc;
    cap_u16(matk)
}

/// status.cpp status_calc_critical (RENEWAL).
pub fn status_calc_critical(_w: &Who, sc: &Sc, mut critical: i32) -> i16 {
    if sc.empty() {
        return cap16(critical, 10, i16::MAX as i32);
    }
    if sc.has(SC_EXPLOSIONSPIRITS) { critical += sc.v2(SC_EXPLOSIONSPIRITS); }
    if sc.has(SC_FORTUNE) { critical += sc.v2(SC_FORTUNE); }
    if sc.has(SC_TRUESIGHT) { critical += sc.v2(SC_TRUESIGHT); }
    if sc.has(SC_CLOAKING) { critical += critical; }
    if sc.has(SC_SPEARQUICKEN) { critical += 3 * sc.v1(SC_SPEARQUICKEN) * 10; }
    if sc.has(SC_TWOHANDQUICKEN) { critical += (2 + sc.v1(SC_TWOHANDQUICKEN)) * 10; }
    if sc.has(SC__INVISIBILITY) { critical += sc.v3(SC__INVISIBILITY) * 10; }
    if sc.has(SC__UNLUCKY) { critical -= sc.v2(SC__UNLUCKY); }
    if sc.has(SC_SOULSHADOW) { critical += 10 * sc.v3(SC_SOULSHADOW); }
    if sc.has(SC_BEYONDOFWARCRY) { critical += sc.v3(SC_BEYONDOFWARCRY); }
    if sc.has(SC_INTENSIVE_AIM) { critical += 300; }
    cap16(critical, 10, i16::MAX as i32)
}

/// status.cpp status_calc_hit (RENEWAL).
pub fn status_calc_hit(_w: &Who, sc: &Sc, mut hit: i32) -> i16 {
    if sc.empty() {
        return cap16(hit, 1, i16::MAX as i32);
    }
    if sc.has(SC_INCHIT) { hit += sc.v1(SC_INCHIT); }
    if sc.has(SC_TRUESIGHT) { hit += sc.v3(SC_TRUESIGHT); }
    if sc.has(SC_HUMMING) { hit += sc.v2(SC_HUMMING); }
    if sc.has(SC_CONCENTRATION) { hit += sc.v3(SC_CONCENTRATION); }
    if sc.has(SC_INSPIRATION) { hit += 12 * sc.v1(SC_INSPIRATION); }
    if sc.has(SC_ADJUSTMENT) { hit -= 30; }
    if sc.has(SC_INCREASING) { hit += 20; }
    if sc.has(SC_MERC_HITUP) { hit += sc.v2(SC_MERC_HITUP); }
    if sc.has(SC_INCHITRATE) { hit += hit * sc.v1(SC_INCHITRATE) / 100; }
    if sc.has(SC_POWERUP) { hit += hit * sc.v2(SC_POWERUP) / 100; }
    if sc.has(SC_BLIND) { hit -= hit * 25 / 100; }
    if sc.has(SC_HEAT_BARREL) { hit -= sc.v4(SC_HEAT_BARREL); }
    if sc.has(SC__GROOMY) { hit -= hit * sc.v3(SC__GROOMY) / 100; }
    if sc.has(SC_FEAR) { hit -= hit * 20 / 100; }
    if sc.has(SC_ASH) { hit -= hit * sc.v2(SC_ASH) / 100; }
    if sc.has(SC_TEARGAS) { hit -= hit * 50 / 100; }
    if sc.has(SC_ILLUSIONDOPING) { hit -= sc.v2(SC_ILLUSIONDOPING); }
    if sc.has(SC_BLESSING) { hit += sc.v1(SC_BLESSING) * 2; }
    if sc.has(SC_TWOHANDQUICKEN) { hit += sc.v1(SC_TWOHANDQUICKEN) * 2; }
    if sc.has(SC_ADRENALINE) { hit += sc.v1(SC_ADRENALINE) * 3 + 5; }
    if sc.has(SC_NIBELUNGEN) && sc.v2(SC_NIBELUNGEN) == RINGNBL_HIT { hit += 50; }
    if sc.has(SC_SOULFALCON) { hit += sc.v3(SC_SOULFALCON); }
    if sc.has(SC_SATURDAYNIGHTFEVER) { hit -= 50 + 50 * sc.v1(SC_SATURDAYNIGHTFEVER); }
    if sc.has(SC_ABYSS_SLAYER) { hit += sc.v3(SC_ABYSS_SLAYER); }
    if sc.has(SC_INTENSIVE_AIM) { hit += 250; }
    cap16(hit, 1, i16::MAX as i32)
}

/// status.cpp status_calc_flee (RENEWAL).
pub fn status_calc_flee(w: &Who, sc: &Sc, mut flee: i32) -> i16 {
    if w.kind == BL_PC {
        use crate::npc::*;
        if mapflag_gvg(w.c, w.map) {
            flee -= flee * crate::conf::gvg_flee_penalty() / 100;
        } else if mapflag(w.c, w.map, MF_BATTLEGROUND) {
            flee -= flee * crate::conf::bg_flee_penalty() / 100;
        }
    }
    if sc.empty() {
        return cap16(flee, 1, i16::MAX as i32);
    }
    if sc.has(SC_POISON_MIST) {
        return 0;
    }
    if sc.has(SC_OVERED_BOOST) {
        // Should be final and unmodifiable by any means
        return sc.v2(SC_OVERED_BOOST) as i16;
    }
    // Fixed value
    if sc.has(SC_INCFLEE) { flee += sc.v1(SC_INCFLEE); }
    if sc.has(SC_WHISTLE) { flee += sc.v2(SC_WHISTLE); }
    if sc.has(SC_WINDWALK) { flee += sc.v2(SC_WINDWALK); }
    if sc.has(SC_VIOLENTGALE) { flee += sc.v2(SC_VIOLENTGALE); }
    if sc.has(SC_MOON_COMFORT) { flee += sc.v2(SC_MOON_COMFORT); }
    if sc.has(SC_CLOSECONFINE) { flee += sc.v3(SC_CLOSECONFINE); }
    if sc.has(SC_ANGRIFFS_MODUS) { flee -= sc.v3(SC_ANGRIFFS_MODUS); }
    if sc.has(SC_ADJUSTMENT) { flee += 30; }
    if sc.has(SC_SPEED) { flee += 10 + sc.v1(SC_SPEED) * 10; }
    if sc.has(SC_GATLINGFEVER) { flee -= sc.v4(SC_GATLINGFEVER); }
    if sc.has(SC_PARTYFLEE) { flee += sc.v1(SC_PARTYFLEE) * 10; }
    if sc.has(SC_MERC_FLEEUP) { flee += sc.v2(SC_MERC_FLEEUP); }
    if sc.has(SC_HALLUCINATIONWALK) { flee += sc.v2(SC_HALLUCINATIONWALK); }
    if sc.has(SC_NPC_HALLUCINATIONWALK) { flee += sc.v2(SC_NPC_HALLUCINATIONWALK); }
    if sc.has(SC_WATER_BARRIER) { flee -= sc.v2(SC_WATER_BARRIER); }
    if sc.has(SC_C_MARKER) { flee -= sc.v3(SC_C_MARKER); }
    if sc.has(SC_WILD_WALK) { flee += sc.v3(SC_WILD_WALK); }
    if sc.has(SC_SPEARQUICKEN) { flee += 2 * sc.v1(SC_SPEARQUICKEN); }
    if sc.has(SC_NIBELUNGEN) && sc.v2(SC_NIBELUNGEN) == RINGNBL_FLEE { flee += 50; }
    // Rate value
    if sc.has(SC_INCFLEERATE) { flee += flee * sc.v1(SC_INCFLEERATE) / 100; }
    if sc.has(SC_AGIUP) { flee += flee * sc.v2(SC_AGIUP) / 100; }
    if sc.has(SC_SPIDERWEB) || sc.has(SC_WIDEWEB) { flee -= flee * 50 / 100; }
    if sc.has(SC_BERSERK) { flee -= flee * 50 / 100; }
    if sc.has(SC_BLIND) { flee -= flee * 25 / 100; }
    if sc.has(SC_FEAR) { flee -= flee * 20 / 100; }
    if sc.has(SC_PARALYSE) && sc.v3(SC_PARALYSE) == 1 { flee -= flee * 10 / 100; }
    if sc.has(SC_INFRAREDSCAN) { flee -= flee * 30 / 100; }
    if sc.has(SC__LAZINESS) { flee -= flee * sc.v3(SC__LAZINESS) / 100; }
    if sc.has(SC_GLOOMYDAY) { flee -= flee * sc.v2(SC_GLOOMYDAY) / 100; }
    if sc.has(SC_SATURDAYNIGHTFEVER) { flee -= 20 + 30 * sc.v1(SC_SATURDAYNIGHTFEVER); }
    if sc.has(SC_WIND_STEP_OPTION) { flee += flee * sc.v2(SC_WIND_STEP_OPTION) / 100; }
    if sc.has(SC_TINDER_BREAKER) || sc.has(SC_TINDER_BREAKER2) { flee -= flee * 50 / 100; }
    if sc.has(SC_ZEPHYR) { flee += sc.v2(SC_ZEPHYR); }
    if sc.has(SC_ASH) { flee -= flee * sc.v4(SC_ASH) / 100; }
    if sc.has(SC_GOLDENE_FERSE) { flee += flee * sc.v2(SC_GOLDENE_FERSE) / 100; }
    if sc.has(SC_SMOKEPOWDER) { flee += flee * 20 / 100; }
    if sc.has(SC_TEARGAS) { flee -= flee * 50 / 100; }
    if sc.has(SC_GROOMING) { flee += sc.v2(SC_GROOMING); }
    cap16(flee, 1, i16::MAX as i32)
}

/// status.cpp status_calc_flee2.
pub fn status_calc_flee2(_w: &Who, sc: &Sc, mut flee2: i32) -> i16 {
    if sc.empty() {
        return cap16(flee2, 10, i16::MAX as i32);
    }
    if sc.has(SC_WHISTLE) { flee2 += sc.v3(SC_WHISTLE); }
    if sc.has(SC__UNLUCKY) { flee2 -= flee2 * sc.v2(SC__UNLUCKY) / 100; }
    if sc.has(SC_HISS) { flee2 += sc.v2(SC_HISS) * 10; }
    if sc.has(SC_DORAM_FLEE2) { flee2 += sc.v1(SC_DORAM_FLEE2); }
    cap16(flee2, 10, i16::MAX as i32)
}

/// status.cpp status_calc_def (RENEWAL).
pub fn status_calc_def(w: &Who, sc: &Sc, mut def: i32) -> i16 {
    if sc.empty() {
        return cap16(def, DEFTYPE_MIN, DEFTYPE_MAX);
    }
    if sc.has(SC_BERSERK) {
        return 0;
    }
    if sc.has(SC_ETERNALCHAOS) {
        return 0;
    }
    if sc.has(SC_KEEPING) {
        return 90;
    }
    if sc.has(SC_NYANGGRASS) {
        if w.kind == BL_PC {
            return 0;
        } else {
            def /= 2;
            return def as i16;
        }
    }
    if sc.has(SC_DEFSET) {
        return sc.v1(SC_DEFSET) as i16;
    }
    if sc.has(SC_DRUMBATTLE) { def += sc.v3(SC_DRUMBATTLE); }
    if sc.has(SC_ASSUMPTIO) { def += sc.v1(SC_ASSUMPTIO) * 50; }
    if w.kind == BL_HOM && sc.has(SC_DEFENCE) { def += sc.v2(SC_DEFENCE); }
    if sc.has(SC_INCDEFRATE) { def += def * sc.v1(SC_INCDEFRATE) / 100; }
    if sc.has(SC_EARTH_INSIGNIA) && sc.v1(SC_EARTH_INSIGNIA) == 2 { def += 50; }
    if sc.has(SC_ODINS_POWER) { def -= 20 * sc.v1(SC_ODINS_POWER); }
    if sc.has(SC_ANGRIFFS_MODUS) { def -= 20 + 10 * sc.v1(SC_ANGRIFFS_MODUS); }
    if sc.has(SC_STONEHARDSKIN) { def += sc.v1(SC_STONEHARDSKIN); }
    if sc.has(SC_STONE) { def /= 2; }
    if sc.has(SC_FREEZE) { def /= 2; }
    if (sc.has(SC_POISON) || sc.has(SC_DPOISON)) && w.kind != BL_PC { def = def * 75 / 100; }
    if sc.has(SC_SIGNUMCRUCIS) { def -= def * sc.v2(SC_SIGNUMCRUCIS) / 100; }
    if sc.has(SC_CONCENTRATION) { def -= def * sc.v4(SC_CONCENTRATION) / 100; }
    if sc.has(SC_SKE) { def /= 2; }
    if sc.has(SC_PROVOKE) && w.kind != BL_PC { def -= def * sc.v3(SC_PROVOKE) / 100; }
    if sc.has(SC_STRIPSHIELD) && w.kind != BL_PC { def -= def * sc.v2(SC_STRIPSHIELD) / 100; }
    if sc.has(SC_FLING) { def -= def * sc.v2(SC_FLING) / 100; }
    if sc.has(SC_FREEZING) { def -= def * (if w.kind == BL_PC { 30 } else { 10 }) / 100; }
    if sc.has(SC_ANALYZE) { def -= def * (14 * sc.v1(SC_ANALYZE)) / 100; }
    if sc.has(SC_NEUTRALBARRIER) { def += def * sc.v2(SC_NEUTRALBARRIER) / 100; }
    if sc.has(SC_PRESTIGE) { def += sc.v3(SC_PRESTIGE); }
    if sc.has(SC_BANDING) && sc.v2(SC_BANDING) > 1 { def += 6 * sc.v1(SC_BANDING); }
    if sc.has(SC_ECHOSONG) { def += sc.v3(SC_ECHOSONG); }
    if sc.has(SC_CAMOUFLAGE) { def -= def * 5 * sc.v3(SC_CAMOUFLAGE) / 100; }
    if sc.has(SC_SOLID_SKIN_OPTION) { def += def * sc.v2(SC_SOLID_SKIN_OPTION) / 100; }
    if sc.has(SC_ROCK_CRUSHER) { def -= def * sc.v2(SC_ROCK_CRUSHER) / 100; }
    if sc.has(SC_POWER_OF_GAIA) { def += def * sc.v2(SC_POWER_OF_GAIA) / 100; }
    if sc.has(SC_ASH) { def -= def * sc.v3(SC_ASH) / 100; }
    if sc.has(SC_OVERED_BOOST) && w.kind == BL_HOM { def -= def * sc.v4(SC_OVERED_BOOST) / 100; }
    if sc.has(SC_SOULGOLEM) { def += sc.v2(SC_SOULGOLEM); }
    if sc.has(SC_STONE_WALL) { def += sc.v2(SC_STONE_WALL); }
    if sc.has(SC_D_MACHINE) { def += sc.v2(SC_D_MACHINE); }
    if sc.has(SC_CLIMAX_CRYIMP) { def += 300; }
    if sc.has(SC_GUARD_STANCE) { def += sc.v2(SC_GUARD_STANCE); }
    if sc.has(SC_ATTACK_STANCE) { def -= sc.v2(SC_ATTACK_STANCE); }
    cap16(def, DEFTYPE_MIN, DEFTYPE_MAX)
}

/// status.cpp status_calc_def2 (RENEWAL). `vit` = status_get_vit(bl): the unit's current battle VIT.
pub fn status_calc_def2(_w: &Who, sc: &Sc, mut def2: i32, vit: i32) -> i16 {
    if sc.empty() {
        return cap16(def2, i16::MIN as i32, i16::MAX as i32);
    }
    if sc.has(SC_BERSERK) {
        return 0;
    }
    if sc.has(SC_ETERNALCHAOS) {
        return 0;
    }
    if sc.has(SC_DEFSET) {
        return sc.v1(SC_DEFSET) as i16;
    }
    if sc.has(SC_SUN_COMFORT) { def2 += sc.v2(SC_SUN_COMFORT); }
    if sc.has(SC_SKA) { def2 += 80; }
    if sc.has(SC_ANGELUS) {
        // The VIT stat bonus is boosted by angelus [RENEWAL]
        def2 += vit / 2 * sc.v2(SC_ANGELUS) / 100;
    }
    if sc.has(SC_POISON) || sc.has(SC_DPOISON) { def2 = def2 * 75 / 100; }
    if sc.has(SC_SKE) { def2 -= def2 * 50 / 100; }
    if sc.has(SC_PROVOKE) { def2 -= def2 * sc.v3(SC_PROVOKE) / 100; }
    if sc.has(SC_JOINTBEAT) {
        let v2 = sc.v2(SC_JOINTBEAT);
        def2 -= def2 * (if v2 & BREAK_SHOULDER != 0 { 50 } else { 0 }) / 100 + def2 * (if v2 & BREAK_WAIST != 0 { 25 } else { 0 }) / 100;
    }
    if sc.has(SC_FLING) { def2 -= def2 * sc.v3(SC_FLING) / 100; }
    if sc.has(SC_ANALYZE) { def2 -= def2 * (14 * sc.v1(SC_ANALYZE)) / 100; }
    if sc.has(SC_ASH) { def2 -= def2 * sc.v3(SC_ASH) / 100; }
    if sc.has(SC_PARALYSIS) { def2 -= def2 * sc.v2(SC_PARALYSIS) / 100; }
    if sc.has(SC_EQC) { def2 -= def2 * sc.v2(SC_EQC) / 100; }
    if sc.has(SC_CAMOUFLAGE) { def2 -= def2 * 5 * sc.v3(SC_CAMOUFLAGE) / 100; }
    cap16(def2, i16::MIN as i32, i16::MAX as i32)
}

/// status.cpp status_calc_mdef (RENEWAL).
pub fn status_calc_mdef(w: &Who, sc: &Sc, mut mdef: i32) -> i16 {
    if sc.empty() {
        return cap16(mdef, DEFTYPE_MIN, DEFTYPE_MAX);
    }
    if sc.has(SC_BERSERK) {
        return 0;
    }
    if sc.has(SC_NYANGGRASS) {
        if w.kind == BL_PC {
            return 0;
        } else {
            return (mdef / 2) as i16;
        }
    }
    if sc.has(SC_MDEFSET) {
        return sc.v1(SC_MDEFSET) as i16;
    }
    if sc.has(SC_EARTH_INSIGNIA) && sc.v1(SC_EARTH_INSIGNIA) == 3 { mdef += 50; }
    if sc.has(SC_ENDURE) && sc.v3(SC_ENDURE) == 0 {
        // It has been confirmed that Eddga card grants 1 MDEF, not 0, not 10, but 1.
        mdef += if sc.v4(SC_ENDURE) == 0 { sc.v1(SC_ENDURE) } else { 1 };
    }
    if sc.has(SC_STONEHARDSKIN) { mdef += sc.v1(SC_STONEHARDSKIN); }
    if sc.has(SC_STONE) { mdef += 25 * mdef / 100; }
    if sc.has(SC_FREEZE) { mdef += 25 * mdef / 100; }
    if sc.has(SC_BURNING) { mdef -= 25 * mdef / 100; }
    if sc.has(SC_NEUTRALBARRIER) { mdef += mdef * sc.v2(SC_NEUTRALBARRIER) / 100; }
    if sc.has(SC_ANALYZE) { mdef -= mdef * (14 * sc.v1(SC_ANALYZE)) / 100; }
    if sc.has(SC_SYMPHONYOFLOVER) { mdef += mdef * sc.v3(SC_SYMPHONYOFLOVER) / 100; }
    if sc.has(SC_ODINS_POWER) { mdef -= 20 * sc.v1(SC_ODINS_POWER); }
    if sc.has(SC_SOULGOLEM) { mdef += sc.v3(SC_SOULGOLEM); }
    if sc.has(SC_STONE_WALL) { mdef += sc.v3(SC_STONE_WALL); }
    if sc.has(SC_CLIMAX_CRYIMP) { mdef += 100; }
    cap16(mdef, DEFTYPE_MIN, DEFTYPE_MAX)
}

/// status.cpp status_calc_mdef2 (RENEWAL).
pub fn status_calc_mdef2(_w: &Who, sc: &Sc, mut mdef2: i32) -> i16 {
    if sc.empty() {
        return cap16(mdef2, i16::MIN as i32, i16::MAX as i32);
    }
    if sc.has(SC_BERSERK) {
        return 0;
    }
    if sc.has(SC_SKA) {
        return 90;
    }
    if sc.has(SC_MDEFSET) {
        return sc.v1(SC_MDEFSET) as i16;
    }
    if sc.has(SC_MINDBREAKER) { mdef2 -= mdef2 * sc.v3(SC_MINDBREAKER) / 100; }
    if sc.has(SC_BURNING) { mdef2 -= mdef2 * 25 / 100; }
    if sc.has(SC_ANALYZE) { mdef2 -= mdef2 * (14 * sc.v1(SC_ANALYZE)) / 100; }
    cap16(mdef2, i16::MIN as i32, i16::MAX as i32)
}

/// status.cpp status_calc_speed. (`sc` null only for units without status changes; every unit here has one.)
pub fn status_calc_speed(w: &Who, sc: &Sc, mut speed: i32) -> u16 {
    let sd = w.pc.as_ref();
    let mut speed_rate: i32 = 100;
    if sd.map(|s| s.permanent_speed).unwrap_or(false) {
        return cap_value(speed, MIN_WALK_SPEED, MAX_WALK_SPEED) as u16;
    }
    if let Some(s) = sd {
        if s.option & OPTION_MADOGEAR != 0 {
            // Mado speed is not affected by other statuses
            let mut val: i32 = 0;
            if w.checkskill(NC_MADOLICENCE) < 5 {
                val = 50 - 10 * w.checkskill(NC_MADOLICENCE);
            } else {
                val -= 25;
            }
            if sc.has(SC_ACCELERATION) {
                val -= 25;
            }
            speed += speed * val / 100;
            return cap_value(speed, MIN_WALK_SPEED, MAX_WALK_SPEED) as u16;
        }
    }
    if let Some(s) = sd.filter(|s| s.casting && (w.checkskill(SA_FREECAST) > 0 || s.skill_id == LG_EXEEDBREAK)) {
        if s.skill_id == LG_EXEEDBREAK {
            speed_rate = 160 - 10 * s.skill_lv as i32;
        } else {
            speed_rate = 175 - 5 * w.checkskill(SA_FREECAST);
        }
    } else {
        let mut val: i32 = 0;
        // GetMoveHasteValue2()
        if sc.has(SC_FUSION) {
            val = 25;
        } else if let Some(s) = sd {
            if s.option & OPTION_RIDING != 0 || s.option & OPTION_DRAGON != 0 {
                val = 25; // Same bonus
            } else if s.option & OPTION_WUGRIDER != 0 {
                val = 15 + 5 * w.checkskill(RA_WUGRIDER);
            } else if sc.has(SC_ALL_RIDING) {
                val = crate::conf::rental_mount_speed_boost();
            }
        }
        speed_rate -= val;
        // GetMoveSlowValue()
        if sd.is_some() && sc.has(SC_HIDING) && w.checkskill(RG_TUNNELDRIVE) > 0 {
            val = 120 - 6 * w.checkskill(RG_TUNNELDRIVE);
        } else if sd.is_some() && sc.has(SC_CHASEWALK) && sc.v3(SC_CHASEWALK) < 0 {
            val = sc.v3(SC_CHASEWALK);
        } else {
            val = 0;
            // Longing for Freedom/Special Singer cancels song/dance penalty (RENEWAL: Ensemble Fatigue)
            if sc.has(SC_ENSEMBLEFATIGUE) {
                val = val.max(sc.v2(SC_ENSEMBLEFATIGUE));
            } else if let Some(s) = sd.filter(|_| sc.has(SC_DANCING)) {
                let bard = (sc.has(SC_SPIRIT) && sc.v2(SC_SPIRIT) == SL_BARDDANCER as i32) as i32;
                let lesson = w.checkskill(if s.sex != 0 { BA_MUSICALLESSON } else { DC_DANCINGLESSON });
                val = val.max(500 - (40 + 10 * bard) * lesson);
            }
            if sc.has(SC_DECREASEAGI) { val = val.max(25); }
            if sc.has(SC_QUAGMIRE) || sc.has(SC_HALLUCINATIONWALK_POSTDELAY) || (sc.has(SC_GLOOMYDAY) && sc.v4(SC_GLOOMYDAY) != 0) { val = val.max(50); }
            if sc.has(SC_DONTFORGETME) { val = val.max(sc.v3(SC_DONTFORGETME)); }
            if sc.has(SC_CURSE) { val = val.max(300); }
            if sc.has(SC_CHASEWALK) { val = val.max(sc.v3(SC_CHASEWALK)); }
            if sc.has(SC_WEDDING) { val = val.max(100); }
            if sc.has(SC_JOINTBEAT) && sc.v2(SC_JOINTBEAT) & (BREAK_ANKLE | BREAK_KNEE) != 0 {
                let v2 = sc.v2(SC_JOINTBEAT);
                val = val.max((if v2 & BREAK_ANKLE != 0 { 50 } else { 0 }) + (if v2 & BREAK_KNEE != 0 { 30 } else { 0 }));
            }
            if sc.has(SC_CLOAKING) && (sc.v4(SC_CLOAKING) & 1) == 0 {
                val = val.max(if sc.v1(SC_CLOAKING) < 3 { 300 } else { 30 - 3 * sc.v1(SC_CLOAKING) });
            }
            if sc.has(SC_GOSPEL) && sc.v4(SC_GOSPEL) == BCT_ENEMY { val = val.max(75); }
            if sc.has(SC_SLOWDOWN) { val = val.max(sc.v1(SC_SLOWDOWN)); }
            if sc.has(SC_GATLINGFEVER) { val = val.max(100); }
            if sc.has(SC_SUITON) { val = val.max(sc.v3(SC_SUITON)); }
            if sc.has(SC_SWOO) { val = val.max(300); }
            if sc.has(SC_FREEZING) { val = val.max(30); }
            if sc.has(SC_MARSHOFABYSS) { val = val.max(sc.v3(SC_MARSHOFABYSS)); }
            if sc.has(SC_CAMOUFLAGE) && sc.v1(SC_CAMOUFLAGE) > 2 { val = val.max(25 * (5 - sc.v1(SC_CAMOUFLAGE))); }
            if sc.has(SC_STEALTHFIELD) { val = val.max(20); }
            if sc.has(SC__LAZINESS) { val = val.max(25); }
            if sc.has(SC_ROCK_CRUSHER_ATK) { val = val.max(sc.v2(SC_ROCK_CRUSHER_ATK)); }
            if sc.has(SC_POWER_OF_GAIA) { val = val.max(sc.v2(SC_POWER_OF_GAIA)); }
            if sc.has(SC_MELON_BOMB) { val = val.max(sc.v2(SC_MELON_BOMB)); }
            if sc.has(SC_REBOUND) { val = val.max(25); }
            if sc.has(SC_B_TRAP) { val = val.max(sc.v3(SC_B_TRAP)); }
            if sc.has(SC_CATNIPPOWDER) { val = val.max(sc.v3(SC_CATNIPPOWDER)); }
            if sc.has(SC_SP_SHA) { val = val.max(sc.v2(SC_SP_SHA)); }
            if sc.has(SC_CREATINGSTAR) { val = val.max(90); }
            if sc.has(SC_SHIELDCHAINRUSH) { val = val.max(20); }
            if sc.has(SC_GROUNDGRAVITY) { val = val.max(20); }
            if let Some(s) = sd {
                if s.speed_rate + s.speed_add_rate > 0 {
                    // Permanent item-based speedup
                    val = val.max(s.speed_rate + s.speed_add_rate);
                }
            }
        }
        speed_rate += val;
        val = 0;
        if sc.has(SC_MARSHOFABYSS) && speed_rate > 150 {
            speed_rate = 150;
        }
        // GetMoveHasteValue1()
        if sc.has(SC_AGIUP) { val = val.max(sc.v1(SC_AGIUP)); }
        if sc.has(SC_INCREASEAGI) { val = val.max(25); }
        if sc.has(SC_WINDWALK) { val = val.max(2 * sc.v1(SC_WINDWALK)); }
        if sc.has(SC_CARTBOOST) { val = val.max(20); }
        if let Some(s) = sd {
            if (s.class_ & crate::db::jobs::MAPID_SECONDMASK) == crate::db::jobs::MAPID_ASSASSIN && w.checkskill(TF_MISS) > 0 {
                val = val.max(w.checkskill(TF_MISS));
            }
        }
        if sc.has(SC_CLOAKING) && (sc.v4(SC_CLOAKING) & 1) == 1 {
            val = val.max(if sc.v1(SC_CLOAKING) >= 10 { 25 } else { 3 * sc.v1(SC_CLOAKING) - 3 });
        }
        if sc.has(SC_BERSERK) { val = val.max(25); }
        if sc.has(SC_RUN) { val = val.max(55); }
        if sc.has(SC_AVOID) { val = val.max(sc.v2(SC_AVOID)); }
        if sc.has(SC_INVINCIBLE) { val = val.max(sc.v3(SC_INVINCIBLE)); }
        if sc.has(SC_CLOAKINGEXCEED) { val = val.max(sc.v3(SC_CLOAKINGEXCEED)); }
        if sc.has(SC_PARALYSE) && sc.v3(SC_PARALYSE) == 0 { val = val.max(50); }
        if sc.has(SC_HOVERING) { val = val.max(10); }
        if sc.has(SC_GN_CARTBOOST) { val = val.max(sc.v2(SC_GN_CARTBOOST)); }
        if sc.has(SC_SWINGDANCE) { val = val.max(sc.v3(SC_SWINGDANCE)); }
        if sc.has(SC_WIND_STEP_OPTION) { val = val.max(sc.v2(SC_WIND_STEP_OPTION)); }
        if sc.has(SC_FULL_THROTTLE) { val = val.max(25); }
        if sc.has(SC_ARCLOUSEDASH) { val = val.max(sc.v3(SC_ARCLOUSEDASH)); }
        if sc.has(SC_DORAM_WALKSPEED) { val = val.max(sc.v1(SC_DORAM_WALKSPEED)); }
        if sc.has(SC_RUSHWINDMILL) { val = val.max(25); }
        if sc.has(SC_EMERGENCY_MOVE) { val = val.max(sc.v2(SC_EMERGENCY_MOVE)); }
        if sc.has(SC_JAWAII_SERENADE) { val = val.max(25); }
        if sc.has(SC_WILD_WALK) { val = val.max(sc.v2(SC_WILD_WALK)); }
        if sc.has(SC_SHADOW_CLOCK) { val = val.max(50); }
        // !FIXME: official items use a single bonus for this [ultramage]
        if let Some(s) = sd {
            if s.speed_rate + s.speed_add_rate < 0 {
                // Permanent item-based speedup
                val = val.max(-(s.speed_rate + s.speed_add_rate));
            }
        }
        speed_rate -= val;
        if speed_rate < 40 {
            speed_rate = 40;
        }
    }
    // GetSpeed()  (NEW_CARTS: pc_iscarton is SC_PUSH_CART)
    if sd.is_some() && sc.has(SC_PUSH_CART) {
        speed += speed * (50 - 5 * w.checkskill(MC_PUSHCART)) / 100;
    }
    if sc.has(SC_PARALYSE) && sc.v3(SC_PARALYSE) == 1 { speed += speed * 50 / 100; }
    if speed_rate != 100 { speed = speed * speed_rate / 100; }
    if sc.has(SC_STEELBODY) { speed = 200; }
    if sc.has(SC_DEFENDER) { speed = speed.max(200); }
    if sc.has(SC_ARMOR) { speed = speed.max(200); }
    if sc.has(SC_WALKSPEED) && sc.v1(SC_WALKSPEED) > 0 { speed = speed * 100 / sc.v1(SC_WALKSPEED); }
    cap_value(speed, MIN_WALK_SPEED, MAX_WALK_SPEED) as u16
}

/// status.cpp status_calc_aspd (RENEWAL_ASPD): fixed (true) or percentage (false) modifiers.
pub fn status_calc_aspd(w: &Who, sc: &Sc, fixed: bool) -> i16 {
    let mut bonus: i32 = 0;
    if sc.empty() {
        return 0;
    }
    if fixed {
        if !sc.has(SC_QUAGMIRE) {
            // !TODO: How does Two-Hand Quicken, Adrenaline Rush, and Spear quick change? (+10%)
            if bonus < 7 && (sc.has(SC_TWOHANDQUICKEN) || sc.has(SC_ONEHAND) || sc.has(SC_MERC_QUICKEN) || sc.has(SC_ADRENALINE) || sc.has(SC_SPEARQUICKEN)) {
                bonus = 7;
            } else if bonus < 6 && sc.has(SC_ADRENALINE2) {
                bonus = 6;
            } else if bonus < 5 && sc.has(SC_FLEET) {
                bonus = 5;
            }
        }
        if sc.has(SC_ASSNCROS) && bonus < sc.v2(SC_ASSNCROS) {
            bonus += sc.v2(SC_ASSNCROS);
        }
        if bonus < 20 && sc.has(SC_MADNESSCANCEL) {
            bonus = 20;
        } else if bonus < 15 && sc.has(SC_BERSERK) {
            bonus = 15;
        }
        // sc_val = the first of ASPDPOTION3..0 present
        for sc_val in [SC_ASPDPOTION3, SC_ASPDPOTION2, SC_ASPDPOTION1, SC_ASPDPOTION0] {
            if sc.has(sc_val) {
                bonus += sc.v1(sc_val);
                break;
            }
        }
    } else {
        if w.kind == BL_PC && sc.has(SC_DONTFORGETME) { bonus -= sc.v2(SC_DONTFORGETME) / 10; }
        if sc.has(SC_ENSEMBLEFATIGUE) { bonus -= sc.v2(SC_ENSEMBLEFATIGUE); }
        if sc.has(SC_STEELBODY) { bonus -= 25; }
        if sc.has(SC_DEFENDER) { bonus -= sc.v4(SC_DEFENDER) / 10; }
        if sc.has(SC_GOSPEL) && sc.v4(SC_GOSPEL) == BCT_ENEMY { bonus -= 75; }
        if sc.has(SC_JOINTBEAT) {
            if sc.v2(SC_JOINTBEAT) & BREAK_WRIST != 0 { bonus -= 25; }
            if sc.v2(SC_JOINTBEAT) & BREAK_KNEE != 0 { bonus -= 10; }
        }
        if sc.has(SC_FREEZING) { bonus -= 30; }
        if sc.has(SC_HALLUCINATIONWALK_POSTDELAY) { bonus -= 50; }
        if sc.has(SC_PARALYSE) && sc.v3(SC_PARALYSE) == 1 { bonus -= 10; }
        if sc.has(SC__BODYPAINT) { bonus -= 5 * sc.v1(SC__BODYPAINT); }
        if sc.has(SC__INVISIBILITY) { bonus -= sc.v2(SC__INVISIBILITY); }
        if sc.has(SC__GROOMY) { bonus -= sc.v2(SC__GROOMY); }
        if sc.has(SC_SWINGDANCE) { bonus += sc.v3(SC_SWINGDANCE); }
        if sc.has(SC_DANCEWITHWUG) { bonus += sc.v3(SC_DANCEWITHWUG); }
        if sc.has(SC_GLOOMYDAY) { bonus -= sc.v3(SC_GLOOMYDAY); }
        if sc.has(SC_GT_CHANGE) { bonus += sc.v3(SC_GT_CHANGE); }
        if sc.has(SC_MELON_BOMB) { bonus -= sc.v3(SC_MELON_BOMB); }
        if sc.has(SC_GOLDENE_FERSE) { bonus += sc.v3(SC_GOLDENE_FERSE); }
        if sc.has(SC_INCASPDRATE) { bonus += sc.v1(SC_INCASPDRATE); }
        if sc.has(SC_GATLINGFEVER) { bonus += sc.v1(SC_GATLINGFEVER); }
        if sc.has(SC_STAR_COMFORT) { bonus += 3 * sc.v1(SC_STAR_COMFORT); }
        if sc.has(SC_WIND_INSIGNIA) && sc.v1(SC_WIND_INSIGNIA) == 2 { bonus += 10; }
        if sc.has(SC_INCREASEAGI) { bonus += sc.v1(SC_INCREASEAGI); }
        if sc.has(SC_NIBELUNGEN) && sc.v2(SC_NIBELUNGEN) == RINGNBL_ASPDRATE { bonus += 20; }
        if sc.has(SC_STARSTANCE) { bonus += sc.v2(SC_STARSTANCE); }
        if w.kind == BL_PC {
            // uint8 skill_lv
            let skill_lv = w.checkskill(BA_MUSICALLESSON) as u8;
            if skill_lv > 0 { bonus += skill_lv as i32; }
            let skill_lv = w.checkskill(RG_PLAGIARISM) as u8;
            if skill_lv > 0 { bonus += skill_lv as i32; }
        }
    }
    bonus as i16
}

/// status.cpp status_calc_fix_aspd.
pub fn status_calc_fix_aspd(_w: &Who, sc: &Sc, mut aspd: i32) -> i16 {
    if sc.empty() {
        return cap_value(aspd, 1, MIN_ASPD) as i16;
    }
    if sc.has(SC_OVERED_BOOST) {
        return cap_value(AMOTION_ZERO_ASPD - sc.v3(SC_OVERED_BOOST) * AMOTION_INTERVAL, 1, MIN_ASPD) as i16;
    }
    if sc.has(SC_GUST_OPTION) || sc.has(SC_BLAST_OPTION) || sc.has(SC_WILD_STORM_OPTION) { aspd -= 50; }
    if sc.has(SC_FIGHTINGSPIRIT) { aspd -= sc.v2(SC_FIGHTINGSPIRIT); }
    if sc.has(SC_SOULSHADOW) { aspd -= 10 * sc.v2(SC_SOULSHADOW); }
    if sc.has(SC_HEAT_BARREL) { aspd -= sc.v1(SC_HEAT_BARREL) * 10; }
    if sc.has(SC_SINCERE_FAITH) { aspd -= 10 * sc.v2(SC_SINCERE_FAITH); }
    cap_value(aspd, 1, MIN_ASPD) as i16
}

/// status.cpp status_calc_aspd_rate (RENEWAL).
pub fn status_calc_aspd_rate(w: &Who, sc: &Sc, mut aspd_rate: i32) -> i16 {
    if sc.empty() {
        return cap16(aspd_rate, 0, i16::MAX as i32);
    }
    let mut max: i32 = 0;
    if sc.has(SC_STAR_COMFORT) { max = sc.v2(SC_STAR_COMFORT); }
    if sc.has(SC_TWOHANDQUICKEN) && max < sc.v2(SC_TWOHANDQUICKEN) { max = sc.v2(SC_TWOHANDQUICKEN); }
    if sc.has(SC_ONEHAND) && max < sc.v2(SC_ONEHAND) { max = sc.v2(SC_ONEHAND); }
    if sc.has(SC_MERC_QUICKEN) && max < sc.v2(SC_MERC_QUICKEN) { max = sc.v2(SC_MERC_QUICKEN); }
    if sc.has(SC_ADRENALINE2) && max < sc.v3(SC_ADRENALINE2) { max = sc.v3(SC_ADRENALINE2); }
    if sc.has(SC_ADRENALINE) && max < sc.v3(SC_ADRENALINE) { max = sc.v3(SC_ADRENALINE); }
    if sc.has(SC_SPEARQUICKEN) && max < sc.v2(SC_SPEARQUICKEN) { max = sc.v2(SC_SPEARQUICKEN); }
    if sc.has(SC_GATLINGFEVER) && max < sc.v2(SC_GATLINGFEVER) { max = sc.v2(SC_GATLINGFEVER); }
    if sc.has(SC_FLEET) && max < sc.v2(SC_FLEET) { max = sc.v2(SC_FLEET); }
    if sc.has(SC_INVINCIBLE) && max < sc.v4(SC_INVINCIBLE) { max = sc.v4(SC_INVINCIBLE); }
    if sc.has(SC_ASSNCROS) && max < sc.v2(SC_ASSNCROS) {
        if w.kind != BL_PC {
            max = sc.v2(SC_ASSNCROS);
        } else {
            let weapon = w.pc.as_ref().map(|p| p.weapon).unwrap_or(0);
            if !matches!(weapon, W_BOW | W_REVOLVER | W_RIFLE | W_GATLING | W_SHOTGUN | W_GRENADE) {
                max = sc.v2(SC_ASSNCROS);
            }
        }
    }
    aspd_rate -= max;
    if sc.has(SC_BERSERK) {
        aspd_rate -= 300;
    } else if sc.has(SC_MADNESSCANCEL) {
        aspd_rate -= 200;
    }
    for i in [SC_ASPDPOTION3, SC_ASPDPOTION2, SC_ASPDPOTION1, SC_ASPDPOTION0] {
        if sc.has(i) {
            aspd_rate -= sc.v2(i);
            break;
        }
    }
    if w.kind == BL_PC && sc.has(SC_DONTFORGETME) { aspd_rate += sc.v2(SC_DONTFORGETME); }
    if sc.has(SC_ENSEMBLEFATIGUE) { aspd_rate += sc.v2(SC_ENSEMBLEFATIGUE); }
    if sc.has(SC_STEELBODY) { aspd_rate += 250; }
    if sc.has(SC_DEFENDER) { aspd_rate += sc.v4(SC_DEFENDER); }
    if sc.has(SC_GOSPEL) && sc.v4(SC_GOSPEL) == BCT_ENEMY { aspd_rate += 250; }
    if sc.has(SC_JOINTBEAT) {
        if sc.v2(SC_JOINTBEAT) & BREAK_WRIST != 0 { aspd_rate += 250; }
        if sc.v2(SC_JOINTBEAT) & BREAK_KNEE != 0 { aspd_rate += 100; }
    }
    if sc.has(SC_FREEZING) { aspd_rate += 300; }
    if sc.has(SC_HALLUCINATIONWALK_POSTDELAY) { aspd_rate += 500; }
    if sc.has(SC_PARALYSE) && sc.v3(SC_PARALYSE) == 1 { aspd_rate += 100; }
    if sc.has(SC__BODYPAINT) { aspd_rate += 50 * sc.v1(SC__BODYPAINT); }
    if sc.has(SC__INVISIBILITY) { aspd_rate += sc.v2(SC__INVISIBILITY) * 10; }
    if sc.has(SC__GROOMY) { aspd_rate += sc.v2(SC__GROOMY) * 10; }
    if sc.has(SC_SWINGDANCE) { aspd_rate -= sc.v3(SC_SWINGDANCE) * 10; }
    if sc.has(SC_DANCEWITHWUG) { aspd_rate -= sc.v3(SC_DANCEWITHWUG) * 10; }
    if sc.has(SC_GLOOMYDAY) { aspd_rate += sc.v3(SC_GLOOMYDAY) * 10; }
    if sc.has(SC_GT_CHANGE) { aspd_rate -= sc.v3(SC_GT_CHANGE) * 10; }
    if sc.has(SC_MELON_BOMB) { aspd_rate += sc.v3(SC_MELON_BOMB) * 10; }
    if sc.has(SC_INCASPDRATE) { aspd_rate -= sc.v1(SC_INCASPDRATE) * 10; }
    if sc.has(SC_GOLDENE_FERSE) { aspd_rate -= sc.v3(SC_GOLDENE_FERSE) * 10; }
    if sc.has(SC_WIND_INSIGNIA) && sc.v1(SC_WIND_INSIGNIA) == 2 { aspd_rate -= 100; }
    if sc.has(SC_STARSTANCE) { aspd_rate -= 10 * sc.v2(SC_STARSTANCE); }
    cap16(aspd_rate, 0, i16::MAX as i32)
}

/// status.cpp status_calc_patk.
pub fn status_calc_patk(_w: &Who, sc: &Sc, mut patk: i32) -> i16 {
    if sc.empty() {
        return cap16(patk, 0, i16::MAX as i32);
    }
    if sc.has(SC_POWERFUL_FAITH) { patk += sc.v3(SC_POWERFUL_FAITH); }
    if sc.has(SC_COMPETENTIA) { patk += sc.v2(SC_COMPETENTIA); }
    if sc.has(SC_ABYSS_SLAYER) { patk += sc.v2(SC_ABYSS_SLAYER); }
    if sc.has(SC_PRON_MARCH) { patk += sc.v2(SC_PRON_MARCH); }
    if sc.has(SC_TEMPERING) { patk += sc.v2(SC_TEMPERING); }
    if sc.has(SC_ATTACK_STANCE) { patk += sc.v3(SC_ATTACK_STANCE); }
    if sc.has(SC_HIDDEN_CARD) { patk += sc.v2(SC_HIDDEN_CARD); }
    if sc.has(SC_TALISMAN_OF_WARRIOR) { patk += sc.v2(SC_TALISMAN_OF_WARRIOR); }
    if sc.has(SC_TEMPORARY_COMMUNION) { patk += sc.v2(SC_TEMPORARY_COMMUNION); }
    if sc.has(SC_BLESSING_OF_M_CREATURES) { patk += sc.v2(SC_BLESSING_OF_M_CREATURES); }
    if sc.has(SC_OVERCOMING_CRISIS) { patk += sc.v2(SC_OVERCOMING_CRISIS); }
    cap16(patk, 0, i16::MAX as i32)
}

/// status.cpp status_calc_smatk.
pub fn status_calc_smatk(_w: &Who, sc: &Sc, mut smatk: i32) -> i16 {
    if sc.empty() {
        return cap16(smatk, 0, i16::MAX as i32);
    }
    if sc.has(SC_COMPETENTIA) { smatk += sc.v2(SC_COMPETENTIA); }
    if sc.has(SC_ABYSS_SLAYER) { smatk += sc.v2(SC_ABYSS_SLAYER); }
    if sc.has(SC_JAWAII_SERENADE) { smatk += sc.v2(SC_JAWAII_SERENADE); }
    if sc.has(SC_SPELL_ENCHANTING) { smatk += sc.v2(SC_SPELL_ENCHANTING); }
    if sc.has(SC_ATTACK_STANCE) { smatk += sc.v3(SC_ATTACK_STANCE); }
    if sc.has(SC_TALISMAN_OF_MAGICIAN) { smatk += sc.v2(SC_TALISMAN_OF_MAGICIAN); }
    if sc.has(SC_T_FIFTH_GOD) { smatk += sc.v2(SC_T_FIFTH_GOD); }
    if sc.has(SC_TEMPORARY_COMMUNION) { smatk += sc.v2(SC_TEMPORARY_COMMUNION); }
    if sc.has(SC_BLESSING_OF_M_CREATURES) { smatk += sc.v2(SC_BLESSING_OF_M_CREATURES); }
    if sc.has(SC_OVERCOMING_CRISIS) { smatk += sc.v2(SC_OVERCOMING_CRISIS); }
    cap16(smatk, 0, i16::MAX as i32)
}

/// status.cpp status_calc_res.
pub fn status_calc_res(w: &Who, sc: &Sc, mut res: i32) -> i16 {
    if sc.empty() {
        return cap16(res, 0, i16::MAX as i32);
    }
    if sc.has(SC_FIRM_FAITH) { res += sc.v3(SC_FIRM_FAITH); }
    if sc.has(SC_D_MACHINE) { res += sc.v3(SC_D_MACHINE); }
    if sc.has(SC_MUSICAL_INTERLUDE) { res += sc.v2(SC_MUSICAL_INTERLUDE); }
    if sc.has(SC_GOLDENE_TONE) { res += sc.v2(SC_GOLDENE_TONE); }
    if sc.has(SC_SHADOW_STRIP) && w.kind != BL_PC { res -= res * sc.v2(SC_SHADOW_STRIP) / 100; }
    if sc.has(SC_AIN_RHAPSODY) { res -= sc.v2(SC_AIN_RHAPSODY); }
    if sc.has(SC_TOXIN_OF_MANDARA) { res -= sc.v2(SC_TOXIN_OF_MANDARA); }
    cap16(res, 0, i16::MAX as i32)
}

/// status.cpp status_calc_mres.
pub fn status_calc_mres(w: &Who, sc: &Sc, mut mres: i32) -> i16 {
    if sc.empty() {
        return cap16(mres, 0, i16::MAX as i32);
    }
    if sc.has(SC_GOLDENE_TONE) { mres += sc.v2(SC_GOLDENE_TONE); }
    if sc.has(SC_SHADOW_STRIP) && w.kind != BL_PC { mres -= mres * sc.v2(SC_SHADOW_STRIP) / 100; }
    if sc.has(SC_GEF_NOCTURN) { mres -= sc.v2(SC_GEF_NOCTURN); }
    cap16(mres, 0, i16::MAX as i32)
}

/// status.cpp status_calc_hplus.
pub fn status_calc_hplus(_w: &Who, sc: &Sc, mut hplus: i32) -> i16 {
    if sc.empty() {
        return cap16(hplus, 0, i16::MAX as i32);
    }
    if sc.has(SC_TEMPORARY_COMMUNION) { hplus += sc.v2(SC_TEMPORARY_COMMUNION); }
    cap16(hplus, 0, i16::MAX as i32)
}

/// status.cpp status_calc_crate.
pub fn status_calc_crate(_w: &Who, sc: &Sc, mut crate_: i32) -> i16 {
    if sc.empty() {
        return cap16(crate_, 0, i16::MAX as i32);
    }
    if sc.has(SC_PRE_ACIES) { crate_ += sc.v2(SC_PRE_ACIES); }
    cap16(crate_, 0, i16::MAX as i32)
}

/// status.cpp status_calc_element.
pub fn status_calc_element(_w: &Who, sc: &Sc, element: i32) -> u8 {
    if sc.empty() {
        return cap_value(element, 0, u8::MAX as i32) as u8;
    }
    if sc.has(SC_FREEZE) || sc.has(SC_CRYSTAL_ARMOR_OPTION) { return ELE_WATER as u8; }
    if sc.has(SC_STONE) || sc.has(SC_STRONG_PROTECTION_OPTION) { return ELE_EARTH as u8; }
    if sc.has(SC_FLAMEARMOR_OPTION) { return ELE_FIRE as u8; }
    if sc.has(SC_EYES_OF_STORM_OPTION) { return ELE_WIND as u8; }
    if sc.has(SC_POISON_SHIELD_OPTION) { return ELE_POISON as u8; }
    if sc.has(SC_BENEDICTIO) { return ELE_HOLY as u8; }
    if sc.has(SC_CHANGEUNDEAD) { return ELE_UNDEAD as u8; }
    if sc.has(SC_ELEMENTALCHANGE) { return sc.v2(SC_ELEMENTALCHANGE) as u8; }
    if sc.has(SC_SHAPESHIFT) { return sc.v2(SC_SHAPESHIFT) as u8; }
    cap_value(element, 0, u8::MAX as i32) as u8
}

/// status.cpp status_calc_element_lv.
pub fn status_calc_element_lv(_w: &Who, sc: &Sc, lv: i32) -> u8 {
    if sc.empty() {
        return cap_value(lv, 1, 4) as u8;
    }
    if sc.has(SC_FREEZE) { return 1; }
    if sc.has(SC_STONE) { return 1; }
    if sc.has(SC_BENEDICTIO) { return 1; }
    if sc.has(SC_CHANGEUNDEAD) { return 1; }
    if sc.has(SC_ELEMENTALCHANGE) { return sc.v1(SC_ELEMENTALCHANGE) as u8; }
    if sc.has(SC_SHAPESHIFT) { return 1; }
    if sc.has(SC__INVISIBILITY) { return 1; }
    if sc.has(SC_FLAMEARMOR_OPTION) || sc.has(SC_CRYSTAL_ARMOR_OPTION) || sc.has(SC_EYES_OF_STORM_OPTION) || sc.has(SC_STRONG_PROTECTION_OPTION) || sc.has(SC_POISON_SHIELD_OPTION) {
        return 1;
    }
    cap_value(lv, 1, 4) as u8
}

/// status.cpp status_calc_attack_element.
pub fn status_calc_attack_element(sc: &Sc, element: i32) -> u8 {
    if sc.empty() {
        return cap_value(element, 0, u8::MAX as i32) as u8;
    }
    if sc.has(SC_ENCHANTARMS) { return sc.v1(SC_ENCHANTARMS) as u8; }
    if sc.has(SC_WATERWEAPON) || (sc.has(SC_WATER_INSIGNIA) && sc.v1(SC_WATER_INSIGNIA) == 2) { return ELE_WATER as u8; }
    if sc.has(SC_EARTHWEAPON) || (sc.has(SC_EARTH_INSIGNIA) && sc.v1(SC_EARTH_INSIGNIA) == 2) { return ELE_EARTH as u8; }
    if sc.has(SC_FIREWEAPON) || (sc.has(SC_FIRE_INSIGNIA) && sc.v1(SC_FIRE_INSIGNIA) == 2) { return ELE_FIRE as u8; }
    if sc.has(SC_WINDWEAPON) || (sc.has(SC_WIND_INSIGNIA) && sc.v1(SC_WIND_INSIGNIA) == 2) { return ELE_WIND as u8; }
    if sc.has(SC_ENCPOISON) { return ELE_POISON as u8; }
    if sc.has(SC_ASPERSIO) { return ELE_HOLY as u8; }
    if sc.has(SC_SHADOWWEAPON) { return ELE_DARK as u8; }
    if sc.has(SC_GHOSTWEAPON) || sc.has(SC__INVISIBILITY) { return ELE_GHOST as u8; }
    if sc.has(SC_TIDAL_WEAPON_OPTION) || sc.has(SC_TIDAL_WEAPON) { return ELE_WATER as u8; }
    cap_value(element, 0, u8::MAX as i32) as u8
}

/// status.cpp status_calc_mode.
pub fn status_calc_mode(_w: &Who, sc: &Sc, mut mode: i32) -> i32 {
    if sc.empty() {
        return cap_value(mode, MD_NONE, i32::MAX);
    }
    if sc.has(SC_MODECHANGE) {
        if sc.v2(SC_MODECHANGE) != 0 {
            mode = (mode & !MD_MASK) | sc.v2(SC_MODECHANGE); // Set mode
        }
        if sc.v3(SC_MODECHANGE) != 0 {
            mode |= sc.v3(SC_MODECHANGE); // Add mode
        }
        if sc.v4(SC_MODECHANGE) != 0 {
            mode &= !sc.v4(SC_MODECHANGE); // Del mode
        }
    }
    cap_value(mode, MD_NONE, i32::MAX)
}

// ---------------------------------------------------------------------------------------------- HP / SP / AP bonuses

/// Player-side inputs of status_get_hpbonus / spbonus / apbonus.
pub struct PcBonusIn {
    pub bonus_hp: i32,
    pub bonus_sp: i32,
    pub bonus_ap: i32,
    pub sex: u8,
}

/// status.cpp status_get_hpbonus.
pub fn status_get_hpbonus(w: &Who, sc: &Sc, pc: Option<&PcBonusIn>, kind: i32) -> i32 {
    let mut bonus: i64 = 0;
    if kind == STATUS_BONUS_FIX {
        if let Some(p) = pc {
            bonus += p.bonus_hp as i64;
            let skill_lv = w.checkskill(CR_TRUST);
            if skill_lv > 0 { bonus += skill_lv as i64 * 200; }
            if w.checkskill(SU_SPRITEMABLE) > 0 { bonus += 1000; }
            if w.checkskill(SU_POWEROFSEA) > 0 {
                bonus += 1000;
                if super::calc::pc_checkskill_summoner(w, SUMMONER_POWER_SEA) >= 20 { bonus += 3000; }
            }
            let skill_lv = w.checkskill(NV_BREAKTHROUGH);
            if skill_lv > 0 { bonus += 350 * skill_lv as i64 + if skill_lv > 4 { 250 } else { 0 }; }
            let skill_lv = w.checkskill(NV_TRANSCENDENCE);
            if skill_lv > 0 { bonus += 350 * skill_lv as i64 + if skill_lv > 4 { 250 } else { 0 }; }
        }
        // Bonus by SC
        if sc.has(SC_INCMHP) { bonus += sc.v1(SC_INCMHP) as i64; }
        if sc.has(SC_EARTH_INSIGNIA) && sc.v1(SC_EARTH_INSIGNIA) == 2 { bonus += 500; }
        if sc.has(SC_PROMOTE_HEALTH_RESERCH) { bonus += sc.v3(SC_PROMOTE_HEALTH_RESERCH) as i64; }
        if sc.has(SC_SOLID_SKIN_OPTION) { bonus += 2000; }
        if sc.has(SC_MARIONETTE) { bonus -= 1000; }
        if sc.has(SC_SWORDCLAN) { bonus += 30; }
        if sc.has(SC_ARCWANDCLAN) { bonus += 30; }
        if sc.has(SC_GOLDENMACECLAN) { bonus += 30; }
        if sc.has(SC_CROSSBOWCLAN) { bonus += 30; }
        if sc.has(SC_ANGELUS) { bonus += sc.v1(SC_ANGELUS) as i64 * 50; }
        if sc.has(SC_OVERCOMING_CRISIS) { bonus += sc.v3(SC_OVERCOMING_CRISIS) as i64; }
    } else if kind == STATUS_BONUS_RATE {
        // Increasing
        if sc.has(SC_INCMHPRATE) { bonus += sc.v1(SC_INCMHPRATE) as i64; }
        if sc.has(SC_APPLEIDUN) { bonus += sc.v2(SC_APPLEIDUN) as i64; }
        if sc.has(SC_DELUGE) { bonus += sc.v2(SC_DELUGE) as i64; }
        if sc.has(SC_BERSERK) { bonus += 200; }
        if sc.has(SC_MERC_HPUP) { bonus += sc.v2(SC_MERC_HPUP) as i64; }
        if sc.has(SC_EPICLESIS) { bonus += sc.v2(SC_EPICLESIS) as i64; }
        if sc.has(SC_FRIGG_SONG) { bonus += sc.v2(SC_FRIGG_SONG) as i64; }
        if sc.has(SC_LERADSDEW) { bonus += sc.v3(SC_LERADSDEW) as i64; }
        if sc.has(SC_FORCEOFVANGUARD) { bonus += 3 * sc.v1(SC_FORCEOFVANGUARD) as i64; }
        if sc.has(SC_INSPIRATION) { bonus += 4 * sc.v1(SC_INSPIRATION) as i64; }
        if sc.has(SC_RAISINGDRAGON) { bonus += sc.v1(SC_RAISINGDRAGON) as i64; }
        if sc.has(SC_GT_REVITALIZE) { bonus += sc.v2(SC_GT_REVITALIZE) as i64; }
        if sc.has(SC_ANGRIFFS_MODUS) { bonus += 5 * sc.v1(SC_ANGRIFFS_MODUS) as i64; }
        if sc.has(SC_PETROLOGY_OPTION) { bonus += sc.v2(SC_PETROLOGY_OPTION) as i64; }
        if sc.has(SC_POWER_OF_GAIA) { bonus += sc.v3(SC_POWER_OF_GAIA) as i64; }
        if sc.has(SC_CURSED_SOIL_OPTION) { bonus += sc.v2(SC_CURSED_SOIL_OPTION) as i64; }
        if sc.has(SC_UPHEAVAL_OPTION) { bonus += sc.v2(SC_UPHEAVAL_OPTION) as i64; }
        if sc.has(SC_LAUDAAGNUS) { bonus += 2 + sc.v1(SC_LAUDAAGNUS) as i64 * 2; }
        if sc.has(SC_NIBELUNGEN) && sc.v2(SC_NIBELUNGEN) == RINGNBL_HPRATE { bonus += 30; }
        if sc.has(SC_LUNARSTANCE) { bonus += sc.v2(SC_LUNARSTANCE) as i64; }
        if sc.has(SC_LUXANIMA) { bonus += sc.v3(SC_LUXANIMA) as i64; }
        if sc.has(SC_FIRM_FAITH) { bonus += sc.v2(SC_FIRM_FAITH) as i64; }
        // Decreasing
        if sc.has(SC_VENOMBLEED) && sc.v3(SC_VENOMBLEED) == 1 { bonus -= 15; }
        if sc.has(SC_BEYONDOFWARCRY) { bonus -= sc.v3(SC_BEYONDOFWARCRY) as i64; }
        if sc.has(SC__WEAKNESS) { bonus -= sc.v2(SC__WEAKNESS) as i64; }
        if sc.has(SC_EQC) { bonus -= sc.v3(SC_EQC) as i64; }
        // Max rate reduce is -100%
        bonus = cap_value(bonus, -100, i32::MAX as i64);
    }
    bonus.min(i32::MAX as i64) as i32
}

/// status.cpp status_get_spbonus.
pub fn status_get_spbonus(w: &Who, sc: &Sc, pc: Option<&PcBonusIn>, kind: i32) -> i32 {
    let mut bonus: i64 = 0;
    if kind == STATUS_BONUS_FIX {
        if let Some(p) = pc {
            bonus += p.bonus_sp as i64;
            let skill_lv = w.checkskill(SL_KAINA);
            if skill_lv > 0 { bonus += 30 * skill_lv as i64; }
            let skill_lv = w.checkskill(RA_RESEARCHTRAP);
            if skill_lv > 0 { bonus += 200 + 20 * skill_lv as i64; }
            let skill_lv = w.checkskill(WM_LESSON);
            if skill_lv > 0 { bonus += 30 * skill_lv as i64; }
            if w.checkskill(SU_SPRITEMABLE) > 0 { bonus += 100; }
            if w.checkskill(SU_POWEROFSEA) > 0 {
                bonus += 100;
                if super::calc::pc_checkskill_summoner(w, SUMMONER_POWER_SEA) >= 20 { bonus += 300; }
            }
            let skill_lv = w.checkskill(NV_BREAKTHROUGH);
            if skill_lv > 0 { bonus += 30 * skill_lv as i64 + if skill_lv > 4 { 50 } else { 0 }; }
            let skill_lv = w.checkskill(NV_TRANSCENDENCE);
            if skill_lv > 0 { bonus += 30 * skill_lv as i64 + if skill_lv > 4 { 50 } else { 0 }; }
        }
        // Bonus by SC
        if sc.has(SC_INCMSP) { bonus += sc.v1(SC_INCMSP) as i64; }
        if sc.has(SC_EARTH_INSIGNIA) && sc.v1(SC_EARTH_INSIGNIA) == 3 { bonus += 50; }
        if sc.has(SC_SWORDCLAN) { bonus += 10; }
        if sc.has(SC_ARCWANDCLAN) { bonus += 10; }
        if sc.has(SC_GOLDENMACECLAN) { bonus += 10; }
        if sc.has(SC_CROSSBOWCLAN) { bonus += 10; }
    } else if kind == STATUS_BONUS_RATE {
        if let Some(p) = pc {
            // uint8 i
            let i = w.checkskill(HP_MEDITATIO) as u8;
            if i > 0 { bonus += i as i64; }
            let i = w.checkskill(HW_SOULDRAIN) as u8;
            if i > 0 { bonus += 2 * i as i64; }
            let i = w.checkskill(if p.sex != 0 { BA_MUSICALLESSON } else { DC_DANCINGLESSON }) as u8;
            if i > 0 { bonus += i as i64; }
            if sc.has(SC_NIBELUNGEN) && sc.v2(SC_NIBELUNGEN) == RINGNBL_SPRATE { bonus += 30; }
        }
        // Increasing
        if sc.has(SC_INCMSPRATE) { bonus += sc.v1(SC_INCMSPRATE) as i64; }
        if sc.has(SC_RAISINGDRAGON) { bonus += sc.v1(SC_RAISINGDRAGON) as i64; }
        if sc.has(SC_SERVICE4U) { bonus += sc.v2(SC_SERVICE4U) as i64; }
        if sc.has(SC_MERC_SPUP) { bonus += sc.v2(SC_MERC_SPUP) as i64; }
        if sc.has(SC_LUXANIMA) { bonus += sc.v3(SC_LUXANIMA) as i64; }
        // Decreasing
        if sc.has(SC_MELODYOFSINK) { bonus -= sc.v3(SC_MELODYOFSINK) as i64; }
        bonus = cap_value(bonus, -100, i32::MAX as i64);
    }
    bonus.min(i32::MAX as i64) as i32
}

/// status.cpp status_get_apbonus.
pub fn status_get_apbonus(_w: &Who, _sc: &Sc, pc: Option<&PcBonusIn>, kind: i32) -> i32 {
    let mut bonus: i64 = 0;
    if kind == STATUS_BONUS_FIX {
        if let Some(p) = pc {
            bonus += p.bonus_ap as i64;
        }
    } else if kind == STATUS_BONUS_RATE {
        bonus = cap_value(bonus, -100, i32::MAX as i64);
    }
    bonus.min(i32::MAX as i64) as i32
}

/// status.cpp status_get_hpbonus_item (no status adds to it).
pub fn status_get_hpbonus_item(_sc: &Sc) -> i32 {
    cap_value(0, -100, i32::MAX)
}

/// status.cpp status_get_spbonus_item.
pub fn status_get_spbonus_item(sc: &Sc) -> i32 {
    let mut bonus: i32 = 0;
    if sc.has(SC_ENERGY_DRINK_RESERCH) { bonus += sc.v3(SC_ENERGY_DRINK_RESERCH); }
    cap_value(bonus, -100, i32::MAX)
}

/// status.cpp status_get_apbonus_item.
pub fn status_get_apbonus_item(_sc: &Sc) -> i32 {
    cap_value(0, -100, i32::MAX)
}

/// status.cpp status_calc_maxhp (non-players).
pub fn status_calc_maxhp(w: &Who, sc: &Sc, mut maxhp: u64) -> u32 {
    let mut rate: i32 = 100;
    maxhp = (maxhp as i64 + status_get_hpbonus(w, sc, None, STATUS_BONUS_FIX) as i64) as u64;
    rate += status_get_hpbonus(w, sc, None, STATUS_BONUS_RATE);
    if rate != 100 {
        maxhp = (maxhp as i64 * rate as i64 / 100) as u64;
    }
    cap_value(maxhp as i64, 1, u32::MAX as i64) as u32
}

/// status.cpp status_calc_maxsp (non-players).
pub fn status_calc_maxsp(w: &Who, sc: &Sc, mut maxsp: u64) -> u32 {
    let mut rate: i32 = 100;
    maxsp = (maxsp as i64 + status_get_spbonus(w, sc, None, STATUS_BONUS_FIX) as i64) as u64;
    rate += status_get_spbonus(w, sc, None, STATUS_BONUS_RATE);
    if rate != 100 {
        maxsp = (maxsp as i64 * rate as i64 / 100) as u64;
    }
    cap_value(maxsp as i64, 1, u32::MAX as i64) as u32
}

/// status.cpp status_calc_maxap (non-players).
pub fn status_calc_maxap(w: &Who, sc: &Sc, mut maxap: u64) -> u32 {
    let mut rate: i32 = 100;
    maxap = (maxap as i64 + status_get_apbonus(w, sc, None, STATUS_BONUS_FIX) as i64) as u64;
    rate += status_get_apbonus(w, sc, None, STATUS_BONUS_RATE);
    if rate != 100 {
        maxap = (maxap as i64 * rate as i64 / 100) as u64;
    }
    cap_value(maxap as i64, 0, u32::MAX as i64) as u32
}
