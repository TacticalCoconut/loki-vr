//! skills/ninja/makibishi.cpp: class SkillMakibishi : public SkillImpl (KO_MAKIBISHI), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface::skill_unitsetting;
use crate::skills::SkillImpl;
use crate::status::consts::SC_STUN;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMakibishi;

impl SkillImpl for SkillMakibishi {
    fn skill_id(&self) -> u16 {
        KO_MAKIBISHI
    }

    /// SkillMakibishi::applyAdditionalEffects: stun, 10% per level.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_STUN, 10 * skill_lv as i32, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillMakibishi::calculateSkillRatio: 20% per level.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -100 + 20 * skill_lv as i32;
    }

    /// SkillMakibishi::castendPos2: level + 2 caltrops, each on a random cell of the 3x3 around the caster (x rolled
    /// before y).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, _x: i16, _y: i16, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        for _ in 0..(skill_lv as i32 + 2) {
            let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let x = s.x - 1 + (c.rnd() % 3) as i16;
            let y = s.y - 1 + (c.rnd() % 3) as i16;
            skill_unitsetting(c, &s, self.skill_id(), skill_lv, x, y, 0);
        }
    }
}
