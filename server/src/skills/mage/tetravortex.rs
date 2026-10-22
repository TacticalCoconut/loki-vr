//! skills/mage/tetravortex.cpp: class SkillTetraVortex : public SkillImpl (WL_TETRAVORTEX) and its four element
//! classes SkillTetraVortexEarth (WL_TETRAVORTEX_GROUND), SkillTetraVortexFire (WL_TETRAVORTEX_FIRE),
//! SkillTetraVortexWater (WL_TETRAVORTEX_WATER), SkillTetraVortexWind (WL_TETRAVORTEX_WIND), rAthena e985006.
//!
//! Tetra Vortex consumes the caster's spheres (a monster uses the four elements) and queues one element hit each
//! 200 ms: a WL_TETRAVORTEX_* timerskill (skill.cpp skill_timerskill, skill/timerskill.rs) whose `x` holds the element
//! bits seen so far (1 fire, 2 water, 4 wind, 8 earth) and `type` the hit's index; the 4th hit (type >= 3) adds one
//! random status of those elements. Above level 5 every element hits the whole splash area instead: the element
//! classes' castendDamageId queues the timerskill on each target, reading the hit index and bits from
//! skill_area_temp[0] / [1].

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_PC};
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::timerskill::skill_addtimerskill;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_SPHERE_1, SC_SPHERE_5};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

use super::summonfireball::{WLS_FIRE, WLS_STONE, WLS_WATER, WLS_WIND};

/// WL_TETRAVORTEX
pub struct SkillTetraVortex;

/// Monster usage: `tetra_skills` (the element skill, its bit).
const TETRA_SKILLS: [(u16, i32); 4] = [(WL_TETRAVORTEX_FIRE, 1), (WL_TETRAVORTEX_WIND, 4), (WL_TETRAVORTEX_WATER, 2), (WL_TETRAVORTEX_GROUND, 8)];

impl SkillImpl for SkillTetraVortex {
    fn skill_id(&self) -> u16 {
        WL_TETRAVORTEX
    }

    /// SkillTetraVortex::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let splash = skill_get_splash(id, skill_lv) as i16;
        if src.kind != BL_PC {
            // Monster usage
            let mut i: u8 = 0;
            for &(skill, bit) in TETRA_SKILLS.iter() {
                if skill_lv > 5 {
                    area::set_temp(0, i as i32);
                    area::set_temp(1, bit);
                    let f = *flag | BCT_ENEMY;
                    foreach_in_range(c, target, splash, BL_CHAR, Wall::None, |c, bl| {
                        area::skill_area_sub(c, bl, src, skill, skill_lv, tick, f, skill_castend_damage_id)
                    });
                } else {
                    let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                    skill_addtimerskill(c, &s, tick + i as Tick * 200, target.id, bit as i16, 0, skill, skill_lv, i as i32, *flag);
                }
                i = i.wrapping_add(1);
            }
        } else if crate::battle::mobif::has_sc(src.kind) {
            // No SC? No spheres
            let mut k: i32 = 0;
            if sc::has_sce(c, src.id, SC_SPHERE_5) {
                // If 5 spheres, remove last one (based on reverse order) and only do 4 actions (Official behavior)
                sc::status_change_end(c, src.id, SC_SPHERE_1, 0);
            }
            // Loop should always be 4 for regular players, but unconditional_skill could be less
            for i in (SC_SPHERE_1..=SC_SPHERE_5).rev() {
                let Some(sphere) = sc::get_sce(c, src.id, i) else { continue };
                let mut subskill: u16 = 0;
                match sphere.val1 {
                    WLS_FIRE => {
                        subskill = WL_TETRAVORTEX_FIRE;
                        k |= 1;
                    }
                    WLS_WIND => {
                        subskill = WL_TETRAVORTEX_WIND;
                        k |= 4;
                    }
                    WLS_WATER => {
                        subskill = WL_TETRAVORTEX_WATER;
                        k |= 2;
                    }
                    WLS_STONE => {
                        subskill = WL_TETRAVORTEX_GROUND;
                        k |= 8;
                    }
                    _ => {}
                }
                let order = (i - SC_SPHERE_5).abs();
                if skill_lv > 5 {
                    area::set_temp(0, order);
                    area::set_temp(1, k);
                    let f = *flag | BCT_ENEMY;
                    foreach_in_range(c, target, splash, BL_CHAR, Wall::None, |c, bl| {
                        area::skill_area_sub(c, bl, src, subskill, skill_lv, tick, f, skill_castend_damage_id)
                    });
                } else {
                    let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                    skill_addtimerskill(c, &s, tick + order as Tick * 200, target.id, k as i16, 0, subskill, skill_lv, order, *flag);
                }
                sc::status_change_end(c, src.id, i, 0);
            }
        }
    }
}

/// The castendDamageId of the four element classes (identical in rAthena): the element hit on one target of the
/// level 6+ area, after skill_area_temp[0] * 200 ms, with the element bits of skill_area_temp[1] and hit index 0.
fn tetra_element_castend_damage_id(c: &Ctx, skill_id: u16, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
    skill_addtimerskill(c, src, tick + area::temp(0) as Tick * 200, target.id, area::temp(1) as i16, 0, skill_id, skill_lv, 0, flag);
}

/// The calculateSkillRatio of the four element classes (identical in rAthena; no RE_LVL_DMOD).
fn tetra_element_skill_ratio(skill_lv: u16, base_skillratio: &mut i32) {
    *base_skillratio += -100 + 800 + 400 * skill_lv as i32;
}

/// WL_TETRAVORTEX_GROUND
pub struct SkillTetraVortexEarth;

impl SkillImpl for SkillTetraVortexEarth {
    fn skill_id(&self) -> u16 {
        WL_TETRAVORTEX_GROUND
    }

    /// SkillTetraVortexEarth::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        tetra_element_skill_ratio(skill_lv, base_skillratio);
    }

    /// SkillTetraVortexEarth::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        tetra_element_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }
}

/// WL_TETRAVORTEX_FIRE
pub struct SkillTetraVortexFire;

impl SkillImpl for SkillTetraVortexFire {
    fn skill_id(&self) -> u16 {
        WL_TETRAVORTEX_FIRE
    }

    /// SkillTetraVortexFire::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        tetra_element_skill_ratio(skill_lv, base_skillratio);
    }

    /// SkillTetraVortexFire::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        tetra_element_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }
}

/// WL_TETRAVORTEX_WATER
pub struct SkillTetraVortexWater;

impl SkillImpl for SkillTetraVortexWater {
    fn skill_id(&self) -> u16 {
        WL_TETRAVORTEX_WATER
    }

    /// SkillTetraVortexWater::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        tetra_element_skill_ratio(skill_lv, base_skillratio);
    }

    /// SkillTetraVortexWater::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        tetra_element_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }
}

/// WL_TETRAVORTEX_WIND
pub struct SkillTetraVortexWind;

impl SkillImpl for SkillTetraVortexWind {
    fn skill_id(&self) -> u16 {
        WL_TETRAVORTEX_WIND
    }

    /// SkillTetraVortexWind::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        tetra_element_skill_ratio(skill_lv, base_skillratio);
    }

    /// SkillTetraVortexWind::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        tetra_element_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }
}
