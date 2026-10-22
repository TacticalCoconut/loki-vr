//! skills/mage/waterball.cpp: class SkillWaterBall : public SkillImpl (WZ_WATERBALL). The balls are
//! skill_timerskill's WZ_WATERBALL case (skill/timerskill.rs), which consumes the cells placed here.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface;
use crate::skill::timerskill::skill_addtimerskill;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillWaterBall;

impl SkillImpl for SkillWaterBall {
    fn skill_id(&self) -> u16 {
        WZ_WATERBALL
    }

    /// SkillWaterBall::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        //Deploy waterball cells, these are used and turned into waterballs via the timerskill
        iface::skill_unitsetting(c, src, id, skill_lv, src.x, src.y, 0);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        skill_addtimerskill(c, &s, tick, target.id, s.x, s.y, id, skill_lv, 0, *flag);
    }

    /// SkillWaterBall::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 30 * skill_lv as i32;
    }
}
