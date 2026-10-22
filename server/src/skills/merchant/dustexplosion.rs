//! skills/merchant/dustexplosion.cpp: class SkillDustExplosion : public SkillImplRecursiveDamageSplash (BO_DUST_EXPLOSION).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::status_get_status_data;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};
use crate::status::consts::SC_RESEARCHREPORT;

/// SkillDustExplosion::SkillDustExplosion() : SkillImplRecursiveDamageSplash(BO_DUST_EXPLOSION).
pub struct SkillDustExplosion;

impl SkillImpl for SkillDustExplosion {
    fn skill_id(&self) -> u16 {
        BO_DUST_EXPLOSION
    }
    crate::splash_skill_defaults!();

    /// SkillDustExplosion::calculateSkillRatio: 500% + 620% per level + POW x 5 ("!TODO: check POW ratio"), +50% + 210% per level under SC_RESEARCHREPORT,
    /// RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = status_get_status_data(c, src);
        *skillratio += -100 + 500 + 620 * skill_lv as i32;
        *skillratio += 5 * sstatus.pow as i32; // !TODO: check POW ratio
        if sce(c, src, SC_RESEARCHREPORT).is_some() {
            *skillratio += 50 + 210 * skill_lv as i32;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillDustExplosion {
    /// splashSearch: the skill packet on the target, then the parent's search.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        crate::skills::splash_search_default(self, c, src, &t, skill_lv, tick, flag);
    }
}
