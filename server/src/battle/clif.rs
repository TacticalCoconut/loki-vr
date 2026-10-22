//! clif.cpp packets of combat: clif_damage (ZC_NOTIFY_ACT3 with clif_calc_delay's endure types and the
//! hallucination damage), clif_arrow_fail, clif_slide / clif_blown (knockback), clif_pvpset, clif_map_type.

use super::*;
use crate::clif::{self, Target};
use crate::map::BL_MOB;
use crate::net::packets::*;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// clif.cpp DEFAULT_ANIMATION_SPEED.
const DEFAULT_ANIMATION_SPEED: i32 = 432;

/// clif.cpp clif_calc_delay: the damage type against a target with endure (4 against single hits, 9 multi-hit).
pub(crate) fn clif_calc_delay(c: &Ctx, bl: &Unit, type_: u8, div: i16, damage: i64, _delay: i32, tick: Tick) -> u8 {
    let mut type_ = type_;
    if damage < 1 {
        return type_;
    }
    // Check if unit has endure
    if !crate::status::status_isendure(c, bl.id, tick, true) {
        return type_;
    }
    // General change of type based on div against target with endure effect
    if div > 1 && type_ == DMG_SINGLE {
        type_ = DMG_MULTI_HIT;
    } else if div < 2 && type_ == DMG_MULTI_HIT {
        type_ = DMG_SINGLE;
    }
    match type_ {
        DMG_ENDURE | DMG_MULTI_HIT_ENDURE | DMG_SPLASH_ENDURE => type_,
        DMG_NORMAL | DMG_CRITICAL | DMG_SINGLE | DMG_MULTI_HIT_CRITICAL => DMG_ENDURE, // DMG_ENDURE despite div > 1
        DMG_MULTI_HIT => DMG_MULTI_HIT_ENDURE,
        DMG_SPLASH => DMG_SPLASH_ENDURE,
        // Custom, unknown result of endure with types not listed
        _ => {
            if div > 1 {
                DMG_MULTI_HIT_ENDURE
            } else {
                DMG_ENDURE
            }
        }
    }
}

/// clif.cpp clif_hallucination_damage: the damage the client of a hallucinating unit displays.
pub(crate) fn clif_hallucination_damage(c: &Ctx, bl: &Unit, damage: i64) -> i64 {
    if !super::mobif::has_sc(bl.kind) {
        return damage;
    }
    let scl = crate::status::sc::load(c, bl.id);
    if scl.empty() || !scl.has(crate::status::consts::SC_HALLUCINATION) || damage == 0 {
        return damage;
    }
    let digit = c.rnd() % 5 + 1;
    (match digit {
        1 => c.rnd() % 10,
        2 => c.rnd() % 100,
        3 => c.rnd() % 1000,
        4 => c.rnd() % 10000,
        _ => c.rnd() % 32767,
    }) as i64
}

/// clif.cpp clif_damage: ZC_NOTIFY_ACT3 (0x08c8), src acts on dst, to the target's area.
#[allow(clippy::too_many_arguments)]
pub fn clif_damage(c: &Ctx, src: &Unit, dst: &Unit, tick: Tick, sdelay: i32, ddelay: i32, sdamage: i64, div: i16, type_: u8, sdamage2: i64, spdamage: bool) {
    let mut damage = sdamage.clamp(i32::MIN as i64, i32::MAX as i64) as i32;
    let mut damage2 = sdamage2.clamp(i32::MIN as i64, i32::MAX as i64) as i32;
    let type_ = clif_calc_delay(c, dst, type_, div, damage as i64 + damage2 as i64, ddelay, tick);
    damage = clif_hallucination_damage(c, dst, damage as i64) as i32;
    damage2 = clif_hallucination_damage(c, dst, damage2 as i64) as i32;
    let mut sdelay = sdelay;
    // Calculate what sdelay to send to the client so it applies damage at the same time as the server
    if conf::synchronize_damage() && src.kind == BL_MOB {
        let clientamotion = (crate::status::status_get_status_data(c, src).clientamotion as i32).max(1);
        sdelay = sdelay * DEFAULT_ANIMATION_SPEED / clientamotion;
        sdelay = sdelay.min(DEFAULT_ANIMATION_SPEED);
    }
    let (pdamage, pdamage2) = if conf::hide_woe_damage() && crate::npc::mapflag_gvg(c, src.map) {
        (if damage != 0 { div as i32 } else { 0 }, if damage2 != 0 { div as i32 } else { 0 })
    } else {
        (damage, damage2)
    };
    let mut p = PACKET_ZC_NOTIFY_ACT {
        packetType: PACKET_ZC_NOTIFY_ACT::ID as i16,
        srcID: src.id as i32,
        targetID: dst.id as i32,
        serverTick: tick as u32 as i32, // client_tick
        srcSpeed: sdelay,
        dmgSpeed: ddelay,
        damage: pdamage,
        isSPDamage: spdamage as i8, // IsSPDamage - Displays blue digits.
        div: div as u16,
        r#type: type_,
        damage2: pdamage2,
    };
    use crate::atcmd_disguise::{disguised, disguised_bl_id};
    if disguised(c, dst) {
        clif::send(c, &p.to_vec(), dst, Target::AreaWos);
        p.targetID = disguised_bl_id(dst.id) as i32;
        clif::send(c, &p.to_vec(), dst, Target::SelfOnly);
    } else {
        clif::send(c, &p.to_vec(), dst, Target::Area);
    }
    if disguised(c, src) {
        p.srcID = disguised_bl_id(src.id) as i32;
        if damage > 0 {
            p.damage = -1;
        }
        if damage2 > 0 {
            p.damage2 = -1;
        }
        clif::send(c, &p.to_vec(), src, Target::SelfOnly);
    }
    if src.id == dst.id {
        // unit_setdir(&src, unit_getdir(&src)): sends the direction again
        if let Some(mut u) = crate::unit::get(c, src.id) {
            let d = crate::unit::getdir(&u);
            crate::unit::setdir(c, &mut u, d, true);
            crate::unit::save(c, &u);
        }
    }
}

/// clif.hpp e_action_failure ARROWFAIL_NO_AMMO; clif.hpp useskill_fail_cause USESKILL_FAIL_NEED_MORE_BULLET.
pub const ARROWFAIL_NO_AMMO: u16 = 0;
pub const USESKILL_FAIL_NEED_MORE_BULLET: u8 = 84;

/// clif.cpp clif_arrow_fail: ZC_ACTION_FAILURE to the player.
pub fn clif_arrow_fail(c: &Ctx, sd: &Unit, type_: u16) {
    let p = PACKET_ZC_ACTION_FAILURE { packetType: PACKET_ZC_ACTION_FAILURE::ID as i16, r#type: type_ };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
}

/// clif.cpp clif_slide: ZC_HIGHJUMP, the instant visual move to (x, y), to the area.
pub fn clif_slide(c: &Ctx, bl: &Unit, x: i16, y: i16) {
    let mut p = PACKET_ZC_HIGHJUMP { packetType: PACKET_ZC_HIGHJUMP::ID, srcId: bl.id, x: x as u16, y: y as u16 };
    clif::send(c, &p.to_vec(), bl, Target::Area);
    if crate::atcmd_disguise::disguised(c, bl) {
        p.srcId = crate::atcmd_disguise::disguised_bl_id(bl.id);
        clif::send(c, &p.to_vec(), bl, Target::SelfOnly);
    }
}

/// clif.cpp clif_blown: clif_slide then clif_fixpos (knockback).
pub fn clif_blown(c: &Ctx, bl: &Unit) {
    clif_slide(c, bl, bl.x, bl.y);
    clif::fixpos(c, bl);
}
