//! skills/mage/summonfireball.cpp: class SkillSummonFireBall : public SkillImpl (WL_SUMMONFB) and class
//! SkillSummonAttackFire : public SkillImpl (WL_SUMMON_ATK_FIRE), rAthena e985006.
//!
//! The four Summon classes (summonfireball.cpp, summonlightningball.cpp, summonwaterball.cpp, summonstone.cpp) have
//! the same castendNoDamageId body but for the sphere element: it is [`summon_sphere_castend_nodamage_id`] here, which
//! the other three files call with their element. The spheres are SC_SPHERE_1..5 with val1 = the element
//! (e_wl_spheres) and val2 = the summon's level; WL_RELEASE (level 2) fires them as WL_SUMMON_ATK_* hits and
//! WL_TETRAVORTEX consumes them.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, fail::USESKILL_FAIL_SUMMON, skill_fail_cause};
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_NONE, SC_SPHERE_1, SC_SPHERE_5};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// skill.hpp enum e_wl_spheres: the element of a Warlock sphere (SC_SPHERE_* val1).
pub const WLS_FIRE: i32 = 0x44;
pub const WLS_WIND: i32 = 0x45;
pub const WLS_WATER: i32 = 0x46;
pub const WLS_STONE: i32 = 0x47;

/// The castendNoDamageId body of SkillSummonFireBall / SkillSummonLightningBall / SkillSummonWaterBall /
/// SkillSummonStone (identical in rAthena but for `element`): level 1 takes the first free sphere slot (fails with
/// USESKILL_FAIL_SUMMON when all five are taken); higher levels replace all five spheres.
pub(crate) fn summon_sphere_castend_nodamage_id(c: &Ctx, skill_id: u16, element: i32, src: &Unit, target: &Unit, skill_lv: u16) {
    // status_change *sc = status_get_sc(src); if (sc == nullptr) return;
    if !crate::battle::mobif::has_sc(src.kind) {
        return;
    }
    // map_session_data* sd = BL_CAST(BL_PC, src)
    let is_pc = src.kind == BL_PC;
    // Set val2. The SC element for this ball
    if skill_lv == 1 {
        let mut sphere = SC_NONE;
        for i in SC_SPHERE_1..=SC_SPHERE_5 {
            if !sc::has_sce(c, src.id, i) {
                sphere = i; // Take the free SC
                break;
            }
        }
        if sphere == SC_NONE {
            if is_pc {
                // No free slots to put SC
                skill_fail_cause(c, src, skill_id, USESKILL_FAIL_SUMMON);
            }
            return;
        }
        sc::sc_start2(c, Some(src), src, sphere, 100, element, skill_lv as i32, skill_get_time(skill_id, skill_lv) as i64);
    } else {
        for i in SC_SPHERE_1..=SC_SPHERE_5 {
            sc::status_change_end(c, src.id, i, 0); // Removes previous type
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            sc::sc_start2(c, Some(&s), &s, i, 100, element, skill_lv as i32, skill_get_time(skill_id, skill_lv) as i64);
        }
    }
    let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
    let t = if target.id == src.id { s.clone() } else { unit::get(c, target.id).unwrap_or_else(|| target.clone()) };
    clif_skill_nodamage(c, Some(&s), &t, skill_id, 0, false);
}

/// WL_SUMMONFB
pub struct SkillSummonFireBall;

impl SkillImpl for SkillSummonFireBall {
    fn skill_id(&self) -> u16 {
        WL_SUMMONFB
    }

    /// SkillSummonFireBall::castendNoDamageId (element WLS_FIRE).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        summon_sphere_castend_nodamage_id(c, self.skill_id(), WLS_FIRE, src, target, skill_lv);
    }
}

/// WL_SUMMON_ATK_FIRE
pub struct SkillSummonAttackFire;

impl SkillImpl for SkillSummonAttackFire {
    fn skill_id(&self) -> u16 {
        WL_SUMMON_ATK_FIRE
    }

    /// SkillSummonAttackFire::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 200;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
