//! skills/acolyte/firstbrand.cpp: class SkillFirstBrand : public SkillImplRecursiveDamageSplash (IQ_FIRST_BRAND).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_FIRST_BRAND;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFirstBrand;

impl SkillImpl for SkillFirstBrand {
    fn skill_id(&self) -> u16 {
        IQ_FIRST_BRAND
    }

    crate::splash_skill_defaults!();

    /// SkillFirstBrand::calculateSkillRatio: 1200% per level + 5 x POW (RE_LVL_DMOD 100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 1200 * skill_lv as i32 + 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillFirstBrand::applyAdditionalEffects: SC_FIRST_BRAND on the target.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_FIRST_BRAND, 100, skill_lv as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
    }
}

impl RecursiveDamageSplash for SkillFirstBrand {
    /// SkillFirstBrand::splashSearch: the animation on the target, then the parent's search.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
