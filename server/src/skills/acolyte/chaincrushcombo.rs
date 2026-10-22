//! skills/acolyte/chaincrushcombo.cpp: class SkillChainCrushCombo : public WeaponSkillImpl (CH_CHAINCRUSH).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_GT_ENERGYGAIN;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillChainCrushCombo;

impl SkillImpl for SkillChainCrushCombo {
    fn skill_id(&self) -> u16 {
        CH_CHAINCRUSH
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillChainCrushCombo::calculateSkillRatio (RENEWAL): 200% per level (RE_LVL_DMOD 100); +50% under Gentle
    /// Touch - Energy Gain.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 200 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        if sce(c, src, SC_GT_ENERGYGAIN).is_some() {
            *skillratio += *skillratio * 50 / 100;
        }
    }
}
