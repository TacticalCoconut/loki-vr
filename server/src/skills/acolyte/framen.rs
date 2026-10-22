//! skills/acolyte/framen.cpp: class SkillFramen : public SkillImplRecursiveDamageSplash (CD_FRAMEN).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::defs::{RC_DEMON, RC_UNDEAD};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFramen;

impl SkillImpl for SkillFramen {
    fn skill_id(&self) -> u16 {
        CD_FRAMEN
    }

    crate::splash_skill_defaults!();

    /// SkillFramen::calculateSkillRatio: 1550% per level + 5% per Fidus Animus level per level + 5 x SPL; +50% per
    /// level on undead and demons (RE_LVL_DMOD 100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let tstatus = crate::status::status_get_status_data(c, target);
        *skillratio += -100 + 1550 * skill_lv as i32;
        *skillratio += 5 * pc_checkskill(c, src, CD_FIDUS_ANIMUS) * skill_lv as i32;
        *skillratio += 5 * sstatus.spl as i32;
        if tstatus.race as i32 == RC_UNDEAD || tstatus.race as i32 == RC_DEMON {
            *skillratio += 50 * skill_lv as i32;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillFramen {
    /// SkillFramen::splashSearch: the animation on the target, then the parent's search.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
