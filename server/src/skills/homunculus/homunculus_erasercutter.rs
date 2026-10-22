//! skills/homunculus/homunculus_erasercutter.cpp: class SkillEraserCutter : public SkillImpl (MH_ERASER_CUTTER).

use crate::battle::calc::Damage;
use crate::battle::BF_MAGIC;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillEraserCutter;

impl SkillImpl for SkillEraserCutter {
    fn skill_id(&self) -> u16 {
        MH_ERASER_CUTTER
    }

    /// SkillEraserCutter::castendDamageId: a magic hit.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        crate::skill::attack::skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }

    /// SkillEraserCutter::calculateSkillRatio: -100 + 450 * lv * base level / 100 + INT (!TODO upstream: confirm the
    /// base level and INT bonus).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -100 + 450 * skill_lv as i32 * crate::status::status_get_lv(c, src) / 100 + sstatus.int_ as i32;
    }
}
