//! skills/summoner/chulhobattering.cpp: class SkillChulhoBattering : public SkillImplRecursiveDamageSplash
//! (SH_CHUL_HO_BATTERING).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillChulhoBattering;

impl SkillImpl for SkillChulhoBattering {
    fn skill_id(&self) -> u16 {
        SH_CHUL_HO_BATTERING
    }

    crate::splash_skill_defaults!();

    /// SkillChulhoBattering::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 550 + 250 * skill_lv as i32;
        *skillratio += 70 * pc_checkskill(c, src, SH_MYSTICAL_CREATURE_MASTERY);
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillChulhoBattering {
    /// SkillChulhoBattering::splashSearch: the skill packet on the target, then the parent's search.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
