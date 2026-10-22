//! skills/ninja/throwhuumashuriken.cpp: class SkillThrowHuumaShuriken : public SkillImplRecursiveDamageSplash
//! (NJ_HUUMA), rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::DMG_SINGLE;
use crate::db::skills::*;
use crate::skill::clif::{clif_skill_damage, DMGVAL_IGNORE};
use crate::skills::novice::status_get_amotion;
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillThrowHuumaShuriken;

impl SkillImpl for SkillThrowHuumaShuriken {
    fn skill_id(&self) -> u16 {
        NJ_HUUMA
    }

    crate::splash_skill_defaults!();

    /// SkillThrowHuumaShuriken::calculateSkillRatio (RENEWAL: -150 + 250% per level).
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -150 + 250 * skill_lv as i32;
    }
}

impl RecursiveDamageSplash for SkillThrowHuumaShuriken {
    /// SkillThrowHuumaShuriken::splashSearch: (RENEWAL) the thrower's animation on the target, then the parent's.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_damage(c, src, target, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, self.skill_id(), skill_lv as i32, DMG_SINGLE);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
