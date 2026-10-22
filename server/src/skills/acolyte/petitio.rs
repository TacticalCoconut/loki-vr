//! skills/acolyte/petitio.cpp: class SkillPetitio : public SkillImplRecursiveDamageSplash (CD_PETITIO).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillPetitio;

impl SkillImpl for SkillPetitio {
    fn skill_id(&self) -> u16 {
        CD_PETITIO
    }

    crate::splash_skill_defaults!();

    /// SkillPetitio::calculateSkillRatio: 1375% per level + 50% per Mace & Book Mastery level per level + 5 x POW
    /// (RE_LVL_DMOD 100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 1375 * skill_lv as i32;
        *skillratio += pc_checkskill(c, src, CD_MACE_BOOK_M) * 50 * skill_lv as i32;
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillPetitio {
    /// SkillPetitio::splashSearch: the animation on the target, then the parent's search.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
