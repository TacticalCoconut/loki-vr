//! skills/merchant/sparkblaster.cpp: class SkillSparkBlaster : public SkillImplRecursiveDamageSplash
//! (MT_SPARK_BLASTER).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillSparkBlaster::SkillSparkBlaster() : SkillImplRecursiveDamageSplash(MT_SPARK_BLASTER).
pub struct SkillSparkBlaster;

impl SkillImpl for SkillSparkBlaster {
    fn skill_id(&self) -> u16 {
        MT_SPARK_BLASTER
    }
    crate::splash_skill_defaults!();

    /// SkillSparkBlaster::calculateSkillRatio: 600 + 1400 * lv %, + 5 * POW, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -100 + 600 + 1400 * skill_lv as i32;
        *base_skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, base_skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillSparkBlaster {
    /// SkillSparkBlaster::splashSearch: the skill packet on the target first, then the parent's search.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
