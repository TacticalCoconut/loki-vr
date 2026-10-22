//! skills/homunculus/homunculus_glanzenspies.cpp: class SkillGlanzenSpies : public SkillImpl (MH_GLANZEN_SPIES).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGlanzenSpies;

impl SkillImpl for SkillGlanzenSpies {
    fn skill_id(&self) -> u16 {
        MH_GLANZEN_SPIES
    }

    /// SkillGlanzenSpies::castendDamageId: a hit of the skill's own type.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        crate::skill::attack::skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
    }

    /// SkillGlanzenSpies::calculateSkillRatio: -100 + 300 + 450 * lv * base level / 100 + VIT (!TODO upstream:
    /// confirm the VIT bonus).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -100 + 300 + 450 * skill_lv as i32 * crate::status::status_get_lv(c, src) / 100 + sstatus.vit as i32;
    }
}
