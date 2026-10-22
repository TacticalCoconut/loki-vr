//! skills/npc/acidbreath.cpp: class SkillAcidBreath : public SkillImpl (NPC_ACIDBREATH), rAthena e985006.
//!
//! The breath classes (acidbreath / darknessbreath / darkpiercing / firebreath / icebreath / icebreath2 /
//! thunderbreath.cpp) have the same castendDamageId; its body is [`breath_castend_damage_id`] here, with what it calls
//! that the module had no port of: skill.cpp skill_attack_area and map.cpp map_foreachinpath (skill/area.rs is their
//! natural home; map_foreachindir is swordman/brandishspear.rs's).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{CellChk, BL_CHAR, BL_SKILL};
use crate::skill::area::{self, foreach_in_range, run_each, Wall};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::{conf, iface, SD_ANIMATION, SD_SPLASH, SKILL_ALTDMG_FLAG};
use crate::skills::swordman::brandishspear::map_foreachindir;
use crate::skills::SkillImpl;
use crate::status::consts::SC_POISON;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillAcidBreath;

impl SkillImpl for SkillAcidBreath {
    fn skill_id(&self) -> u16 {
        NPC_ACIDBREATH
    }

    /// SkillAcidBreath::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_POISON, 70, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillAcidBreath::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 100 * (skill_lv as i32 - 1);
    }

    /// SkillAcidBreath::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        breath_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillAcidBreath::modifyHitRate: `hit_rate *= 2` on the int16.
    fn modify_hit_rate(&self, _c: &Ctx, hit_rate: &mut i16, _src: &Unit, _target: &Unit, _skill_lv: u16) {
        *hit_rate = (*hit_rate as i32 * 2) as i16;
    }
}

/// The castendDamageId of the breath classes (SkillAcidBreath / SkillDarknessBreath / SkillDarkPiercing /
/// SkillFireBreath / SkillIceBreath / SkillIceBreath2 / SkillThunderBreath): skill_attack_area on the units of the
/// path from the caster towards the target (skill_eightpath_algorithm: the eight-direction path, and the target
/// itself when the path hits nothing; else the line to the target).
pub(crate) fn breath_castend_damage_id(c: &Ctx, skill_id: u16, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
    let id = skill_id;
    area::set_temp(1, target.id as i32);
    let atk_type = skill_get_type(id);
    // (the int16 range: skill_get_splash's int32 converted)
    let splash = skill_get_splash(id, skill_lv) as i16;
    let maxcount = skill_get_maxcount(id, skill_lv);
    if conf::skill_eightpath_algorithm() {
        // Use official AoE algorithm
        let n = map_foreachindir(c, src.map, src.x, src.y, target.x, target.y, splash, maxcount, 0, area::splash_target(src), |c, bl| {
            skill_attack_area(c, bl, atk_type, src, src, id, skill_lv, tick, flag, BCT_ENEMY)
        });
        if n == 0 {
            // These skills hit at least the target if the AoE doesn't hit
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            skill_attack(c, atk_type, &s, &s, &t, id, skill_lv, tick, flag);
        }
    } else {
        map_foreachinpath(c, src.map, src.x, src.y, target.x, target.y, splash, maxcount, area::splash_target(src), |c, bl| {
            skill_attack_area(c, bl, atk_type, src, src, id, skill_lv, tick, flag, BCT_ENEMY)
        });
    }
}

/// skill.cpp skill_attack_area(bl, { atk_type, src, dsrc, skill_id, skill_lv, tick, flag, type }): the splash hit of
/// an area around the 'first' target. The first target (skill_area_temp[1]) skips the target checks and always
/// receives damage; SS_FUUMAKOUCHIKU on a SS_FUUMASHOUAKU unit makes it explode (1); the others need
/// battle_check_target from dsrc and status_check_skilluse (flag 2) and are hit without the skill animation, except
/// for WZ_FROSTNOVA (not on the caster's cell nor on Land Protector) and the breath skills. Returns the damage (int32).
/// `src` / `dsrc` are read fresh (an earlier target's hit may have changed them).
#[allow(clippy::too_many_arguments)]
pub(crate) fn skill_attack_area(c: &Ctx, bl: &Unit, atk_type: i32, src: &Unit, dsrc: &Unit, skill_id: u16, skill_lv: u16, tick: Tick, flag: i32, type_: i32) -> i32 {
    if sc::status_isdead(c, bl) {
        return 0;
    }
    let src = unit::get(c, src.id).unwrap_or_else(|| src.clone());
    let dsrc = if dsrc.id == src.id { src.clone() } else { unit::get(c, dsrc.id).unwrap_or_else(|| dsrc.clone()) };
    if area::temp(1) == bl.id as i32 {
        // This is the target of the skill, do a full attack and skip target checks.
        return skill_attack(c, atk_type, &src, &dsrc, bl, skill_id, skill_lv, tick, flag) as i32;
    }
    if skill_id == SS_FUUMAKOUCHIKU && bl.kind == BL_SKILL {
        // skill_unit* unit = (skill_unit*)bl; unit->group == nullptr: return 0 (skill units: skillunit agent)
        let Some(group_skill_id) = iface::skill_unit_group_skill_id(c, bl) else { return 0 };
        if group_skill_id == SS_FUUMASHOUAKU {
            let f = flag | BCT_ENEMY | SD_SPLASH | SKILL_ALTDMG_FLAG | 1;
            foreach_in_range(c, bl, skill_get_splash(SS_FUUMAKOUCHIKU, skill_lv) as i16, BL_CHAR, Wall::None, |c, t| {
                area::skill_area_sub(c, t, &src, SS_FUUMAKOUCHIKU, skill_lv, tick, f, skill_castend_damage_id)
            });
            iface::skill_delunit(c, bl.id);
            return 1;
        }
    }
    if crate::battle::target::battle_check_target(c, &dsrc, bl, type_) <= 0 || !crate::status::check::status_check_skilluse(c, None, Some(bl), skill_id, 2) {
        return 0;
    }
    match skill_id {
        // Skills that don't require the animation to be removed
        WZ_FROSTNOVA | NPC_ACIDBREATH | NPC_DARKNESSBREATH | NPC_FIREBREATH | NPC_ICEBREATH | NPC_ICEBREATH2 | NPC_THUNDERBREATH => {
            if skill_id == WZ_FROSTNOVA {
                if src.x == bl.x && src.y == bl.y {
                    return 0; // Does not hit current cell
                }
                if iface::map_getcell_landprotector(c, bl.map, bl.x, bl.y) {
                    return 0; // Attack should not happen if the target is on Land Protector
                }
                // [[fallthrough]]
            }
            skill_attack(c, atk_type, &src, &dsrc, bl, skill_id, skill_lv, tick, flag) as i32
        }
        // Area-splash, disable skill animation.
        _ => skill_attack(c, atk_type, &src, &dsrc, bl, skill_id, skill_lv, tick, flag | SD_ANIMATION) as i32,
    }
}

/// map.cpp MAGNITUDE2(x0, y0, x1, y1): the square of the distance between two points.
fn magnitude2(x0: i32, y0: i32, x1: i32, y1: i32) -> i32 {
    (x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)
}

/// map.cpp `range *= range << 8` on map_foreachinpath's int16 range: the product is stored back into the int16.
fn path_range_sq(range: i16) -> i32 {
    let r = range as i32;
    r.wrapping_mul(r << 8) as i16 as i32
}

/// map.cpp map_foreachinpath: `f` on every unit of `kinds` within `range` of the line from (x0, y0) to (x1, y1)
/// (sharp shooting 3 [Skotlex]); with `length`, the end point is moved to `length` cells from the start; targets
/// beyond (x1, y1) need line of sight from (x0, y0). rAthena walks the blocks (BLOCK_SIZE cells) that cover the area
/// with no cell test, so every unit of those blocks is a candidate, in its block order (the other kinds, then the
/// monsters); the int32 math, the int16 `range *= range << 8` and the float square root are rAthena's. Returns the sum
/// of what `f` returned.
#[allow(clippy::too_many_arguments)]
pub(crate) fn map_foreachinpath(c: &Ctx, m: u16, x0: i16, y0: i16, x1: i16, y1: i16, range: i16, length: i32, kinds: u16, f: impl FnMut(&Ctx, &Unit) -> i32) -> i32 {
    let (x0, y0, x1, y1) = (x0 as i32, y0 as i32, x1 as i32, y1 as i32);
    let (mut mx0, mut mx1, mut my0, mut my1) = (x0, x1, y0, y1);
    // Avoid needless calculations by not getting the sqrt right away.
    let magnitude2_ = magnitude2(x0, y0, x1, y1);
    let mut len_limit = magnitude2_;
    if magnitude2_ < 1 {
        return 0; // Same begin and ending point, can't trace path.
    }
    if length != 0 {
        // Adjust final position to fit in the given area.
        let k = (magnitude2_ as f32).sqrt() as i32;
        mx1 = x0 + (x1 - x0) * length / k;
        my1 = y0 + (y1 - y0) * length / k;
        len_limit = magnitude2(x0, y0, mx1, my1);
    }
    // Expand target area to cover range.
    let r = range as i32;
    if mx0 > mx1 {
        mx0 += r;
        mx1 -= r;
    } else {
        mx0 -= r;
        mx1 += r;
    }
    if my0 > my1 {
        my0 += r;
        my1 -= r;
    } else {
        my0 -= r;
        my1 += r;
    }
    // The two fors assume mx0 < mx1 && my0 < my1
    if mx0 > mx1 {
        std::mem::swap(&mut mx0, &mut mx1);
    }
    if my0 > my1 {
        std::mem::swap(&mut my0, &mut my1);
    }
    let Some(mapdata) = crate::data::maps().get(m) else { return 0 };
    mx0 = mx0.max(0);
    my0 = my0.max(0);
    mx1 = mx1.min(mapdata.xs as i32 - 1);
    my1 = my1.min(mapdata.ys as i32 - 1);
    // Values are shifted later on for higher precision using int32 math.
    let range_sq = path_range_sq(range);
    // The blocks `for (by = my0 / BLOCK_SIZE; …)` / `for (bx = mx0 / BLOCK_SIZE; …)` walk.
    let bs = crate::map::BLOCK_SIZE as i32;
    let (bx0, bx1, by0, by1) = (mx0 / bs, mx1 / bs, my0 / bs, my1 / bs);
    if bx0 > bx1 || by0 > by1 {
        return 0;
    }
    let (cx0, cy0) = (bx0 * bs, by0 * bs);
    let (cx1, cy1) = ((bx1 * bs + bs - 1).min(mapdata.xs as i32 - 1), (by1 * bs + bs - 1).min(mapdata.ys as i32 - 1));
    let ids: Vec<u32> = crate::map::units_in_area(c, m, cx0 as i16, cy0 as i16, cx1 as i16, cy1 as i16, kinds)
        .into_iter()
        .filter(|bl| {
            let (mut xi, mut yi) = (bl.x as i32, bl.y as i32);
            let mut k = (xi - x0) * (x1 - x0) + (yi - y0) * (y1 - y0);
            if k < 0 || k > len_limit {
                return false; // Since more skills use this, check for ending point as well.
            }
            if k > magnitude2_ && !crate::path::path_search_long(m, x0 as i16, y0 as i16, bl.x, bl.y, CellChk::ChkWall) {
                return false; // Targets beyond the initial ending point need the wall check.
            }
            // All these shifts are to increase the precision of the intersection point and distance considering how
            // it's int32 math.
            k = (k << 4) / magnitude2_; // k will be between 1~16 instead of 0~1
            xi <<= 4;
            yi <<= 4;
            let xu = (x0 << 4) + k * (x1 - x0);
            let yu = (y0 << 4) + k * (y1 - y0);
            k = magnitude2(xi, yi, xu, yu);
            // If all dot coordinates were <<4 the square of the magnitude is <<8
            k <= range_sq
        })
        .map(|bl| bl.id)
        .collect();
    run_each(c, ids, f)
}

#[cfg(test)]
mod tests {
    /// map_foreachinpath's `range *= range << 8` keeps the int16 product: 11 cells still fit, 12 wrap negative.
    #[test]
    fn path_range_is_stored_in_an_int16() {
        assert_eq!(super::path_range_sq(1), 256);
        assert_eq!(super::path_range_sq(11), 30976);
        assert_eq!(super::path_range_sq(12), -28672);
    }
}
