//! skills/acolyte/thirdconsecration.cpp: class SkillThirdConsecration : public SkillImplRecursiveDamageSplash (IQ_THIRD_CONSECRATION).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_SECOND_BRAND;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillThirdConsecration;

impl SkillImpl for SkillThirdConsecration {
    fn skill_id(&self) -> u16 {
        IQ_THIRD_CONSECRATION
    }

    crate::splash_skill_defaults!();

    /// SkillThirdConsecration::calculateSkillRatio: 1250% per level + 10 x POW (RE_LVL_DMOD 100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 1250 * skill_lv as i32;
        *skillratio += 10 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillThirdConsecration::applyAdditionalEffects: the target's SC_SECOND_BRAND ends.
    fn apply_additional_effects(&self, c: &Ctx, _src: &Unit, target: &Unit, _skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::status_change_end(c, target.id, SC_SECOND_BRAND, 0);
    }
}

impl RecursiveDamageSplash for SkillThirdConsecration {
    /// SkillThirdConsecration::splashSearch: the animation on the target, then the parent's search.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
