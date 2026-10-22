//! skills/homunculus/homunculus_twistercutter.cpp: class SkillTwisterCutter : public SkillImpl (MH_TWISTER_CUTTER).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillTwisterCutter;

impl SkillImpl for SkillTwisterCutter {
    fn skill_id(&self) -> u16 {
        MH_TWISTER_CUTTER
    }

    /// SkillTwisterCutter::castendDamageId: a hit of the skill's own type.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        crate::skill::attack::skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
    }

    /// SkillTwisterCutter::calculateSkillRatio: -100 + 480 * lv * base level / 100 + INT (!TODO upstream: confirm
    /// the base level and INT bonus).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -100 + 480 * skill_lv as i32 * crate::status::status_get_lv(c, src) / 100 + sstatus.int_ as i32;
    }
}
