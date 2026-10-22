//! skills/merchant/mysterypowder.cpp: class SkillMysteryPowder : public SkillImplRecursiveDamageSplash (BO_MYSTERY_POWDER).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::status_get_status_data;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};
use crate::status::sc;

/// SkillMysteryPowder::SkillMysteryPowder() : SkillImplRecursiveDamageSplash(BO_MYSTERY_POWDER).
pub struct SkillMysteryPowder;

impl SkillImpl for SkillMysteryPowder {
    fn skill_id(&self) -> u16 {
        BO_MYSTERY_POWDER
    }
    crate::splash_skill_defaults!();

    /// SkillMysteryPowder::calculateSkillRatio: 1500% + 4000% per level + POW x 5 ("!TODO: check POW ratio"), RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = status_get_status_data(c, src);
        *skillratio += -100 + 1500 + 4000 * skill_lv as i32;
        *skillratio += 5 * sstatus.pow as i32; // !TODO: check POW ratio
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillMysteryPowder {
    /// SkillMysteryPowder::splashSearch: the skill packet on the target, the skill's status on the caster, then the
    /// parent's search.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        sc::sc_start(c, Some(&s), &s, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let s = unit::get(c, src.id).unwrap_or(s);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        crate::skills::splash_search_default(self, c, &s, &t, skill_lv, tick, flag);
    }
}
