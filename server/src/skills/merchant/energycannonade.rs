//! skills/merchant/energycannonade.cpp: class SkillEnergyCannonade : public SkillImplRecursiveDamageSplash
//! (MT_ENERGY_CANNONADE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillEnergyCannonade::SkillEnergyCannonade() : SkillImplRecursiveDamageSplash(MT_ENERGY_CANNONADE).
pub struct SkillEnergyCannonade;

impl SkillImpl for SkillEnergyCannonade {
    fn skill_id(&self) -> u16 {
        MT_ENERGY_CANNONADE
    }
    crate::splash_skill_defaults!();

    /// SkillEnergyCannonade::calculateSkillRatio: 250 + 750 * lv %, + 5 * POW ("!TODO: check POW ratio"),
    /// RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -100 + 250 + 750 * skill_lv as i32;
        *base_skillratio += 5 * sstatus.pow as i32; // !TODO: check POW ratio
        re_lvl_dmod(c, src, base_skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillEnergyCannonade {
    /// SkillEnergyCannonade::splashSearch: the skill packet on the target first, then the parent's search.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
