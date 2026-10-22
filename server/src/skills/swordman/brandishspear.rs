//! skills/swordman/brandishspear.cpp: class SkillBrandishSpear : public SkillImpl (KN_BRANDISHSPEAR), and map.cpp
//! map_foreachindir, the directional area it searches (its only user in the ported families; skill/area.rs is the
//! natural home of the map_foreachin* helpers).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::CellChk;
use crate::path::{direction_diagonal, DIRX, DIRY, DIR_EAST, DIR_MAX};
use crate::skill::area::{self, run_each};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillBrandishSpear;

impl SkillImpl for SkillBrandishSpear {
    fn skill_id(&self) -> u16 {
        KN_BRANDISHSPEAR
    }

    /// SkillBrandishSpear::castendNoDamageId (RENEWAL): one directional area from the caster towards the target.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let f = *flag | BCT_ENEMY;
        map_foreachindir(c, src.map, src.x, src.y, target.x, target.y, skill_get_splash(id, skill_lv) as i16, skill_get_maxcount(id, skill_lv), 0, area::splash_target(src), |c, bl| {
            area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id)
        });
    }

    /// SkillBrandishSpear::castendDamageId (RENEWAL).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
    }

    /// SkillBrandishSpear::calculateSkillRatio (RENEWAL).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -100 + 400 + 100 * skill_lv as i32 + sstatus.str as i32 * 3;
    }
}

/// map.cpp map_foreachindir: `f` on every unit of `kinds` in the path from (x0, y0) towards (x1, y1) — `length` cells
/// long starting `offset` cells away, `range` cells to each side — that is in line of sight of (x0, y0); units are
/// collected first in rAthena's block order (the other kinds, then the monsters) and `f` runs on those still on the
/// map. Returns the sum of what `f` returned.
#[allow(clippy::too_many_arguments)]
pub(crate) fn map_foreachindir(c: &Ctx, m: u16, x0: i16, y0: i16, x1: i16, y1: i16, range: i16, length: i32, offset: i32, kinds: u16, f: impl FnMut(&Ctx, &Unit) -> i32) -> i32 {
    let dir = crate::map::calc_dir_xy(x0, y0, x1, y1, DIR_EAST);
    let dx = DIRX[dir as usize] as i32;
    let dy = DIRY[dir as usize] as i32;
    let (mut length, mut offset) = (length, offset);
    if range < 0 {
        return 0;
    }
    if length < 1 {
        return 0;
    }
    if offset < 0 {
        return 0;
    }
    // Special offset handling for diagonal paths
    if offset != 0 && direction_diagonal(dir) {
        // So that diagonal paths can attach to each other, we have to work with half-tile offsets
        offset = (2 * offset) - 1;
        // To get the half-tiles we need to increase length by one
        length += 1;
    }
    let Some(mapdata) = crate::data::maps().get(m) else { return 0 };
    let (x0i, y0i) = (x0 as i32, y0 as i32);
    let step = (dir % 2) as i32 + 1;
    // Get area that needs to be checked
    let mut mx0 = x0i + dx * (offset / step);
    let mut my0 = y0i + dy * (offset / step);
    let mut mx1 = x0i + dx * (offset / step + length - 1);
    let mut my1 = y0i + dy * (offset / step + length - 1);
    // The following assumes mx0 < mx1 && my0 < my1
    if mx0 > mx1 {
        std::mem::swap(&mut mx0, &mut mx1);
    }
    if my0 > my1 {
        std::mem::swap(&mut my0, &mut my1);
    }
    // Apply width to the path by turning 90 degrees
    let r = range as i32;
    let side = ((dir + 2) % DIR_MAX) as usize;
    mx0 -= (r * DIRX[side] as i32).abs();
    my0 -= (r * DIRY[side] as i32).abs();
    mx1 += (r * DIRX[side] as i32).abs();
    my1 += (r * DIRY[side] as i32).abs();
    mx0 = mx0.max(0);
    my0 = my0.max(0);
    mx1 = mx1.min(mapdata.xs as i32 - 1);
    my1 = my1.min(mapdata.ys as i32 - 1);
    if mx0 > mx1 || my0 > my1 {
        return 0; // (no cell of the map in the area: rAthena's block walk finds no unit inside it)
    }
    let diagonal = direction_diagonal(dir);
    let ids: Vec<u32> = crate::map::units_in_area(c, m, mx0 as i16, my0 as i16, mx1 as i16, my1 as i16, kinds)
        .into_iter()
        .filter(|bl| {
            // (the monsters' block list is walked only when `type & BL_MOB`: units_in_area selects by kind)
            // What matters now is the relative x and y from the start point
            let mut rx = bl.x as i32 - x0i;
            let mut ry = bl.y as i32 - y0i;
            // Do not hit source cell
            if !crate::skill::conf::skill_eightpath_same_cell() && rx == 0 && ry == 0 {
                return false;
            }
            // This turns it so that the area that is hit is always with positive rx and ry
            rx *= dx;
            ry *= dy;
            // These checks only need to be done for diagonal paths
            if diagonal {
                // Check for length
                if (rx + ry < offset) || (rx + ry > 2 * (length + (offset / 2) - 1)) {
                    return false;
                }
                // Check for width
                if (rx - ry).abs() > 2 * r {
                    return false;
                }
            }
            // Everything else ok, check for line of sight from source
            crate::path::path_search_long(m, x0, y0, bl.x, bl.y, CellChk::ChkWall)
        })
        .map(|bl| bl.id)
        .collect();
    run_each(c, ids, f)
}
