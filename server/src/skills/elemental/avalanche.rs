//! skills/elemental/avalanche.cpp: class SkillAvalanche : public SkillImplRecursiveDamageSplash (EM_EL_AVALANCHE), rAthena e985006.

use super::add_master_level_ratio;
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillAvalanche;

impl SkillImpl for SkillAvalanche {
    fn skill_id(&self) -> u16 {
        EM_EL_AVALANCHE
    }

    crate::splash_skill_defaults!();

    /// SkillAvalanche::calculateSkillRatio: 450%; cast by an elemental, raised by its master's base level percent.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -100 + 450;
        add_master_level_ratio(c, src, base_skillratio);
    }
}

impl RecursiveDamageSplash for SkillAvalanche {
    /// SkillAvalanche::splashSearch: the skill animation on the target, then the parent's search.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
