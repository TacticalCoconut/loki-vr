//! skills/summoner/scratch.cpp: class SkillScratch : public SkillImplRecursiveDamageSplash (SU_SCRATCH).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_BLEEDING;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillScratch;

impl SkillImpl for SkillScratch {
    fn skill_id(&self) -> u16 {
        SU_SCRATCH
    }

    crate::splash_skill_defaults!();

    /// SkillScratch::applyAdditionalEffects: Bleeding (level * 10 + 70 %, val2 the caster).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let lv = skill_lv as i32;
        sc::sc_start2(c, Some(src), target, SC_BLEEDING, lv * 10 + 70, lv, src.id as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
    }

    /// SkillScratch::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -50 + 50 * skill_lv as i32;
    }
}

impl RecursiveDamageSplash for SkillScratch {
    /// SkillScratch::splashSearch: the skill packet on the target, then the parent's search.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
