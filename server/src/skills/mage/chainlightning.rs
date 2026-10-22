//! skills/mage/chainlightning.cpp: class SkillChainLightning : public SkillImpl (WL_CHAINLIGHTNING) and class
//! SkillChainLightningAttack : public SkillImpl (WL_CHAINLIGHTNING_ATK), rAthena e985006. The bolts are
//! WL_CHAINLIGHTNING_ATK timerskills (skill.cpp skill_timerskill, ported in skill/timerskill.rs), each one bouncing to
//! another enemy.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::timerskill::skill_addtimerskill;
use crate::skills::novice::{re_lvl_dmod, status_get_amotion};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// WL_CHAINLIGHTNING
pub struct SkillChainLightning;

impl SkillImpl for SkillChainLightning {
    fn skill_id(&self) -> u16 {
        WL_CHAINLIGHTNING
    }

    /// SkillChainLightning::castendNoDamageId: the first bolt after the caster's attack motion.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        skill_addtimerskill(c, src, tick + status_get_amotion(c, src) as Tick, target.id, 0, 0, WL_CHAINLIGHTNING_ATK, skill_lv, 0, 0);
    }
}

/// WL_CHAINLIGHTNING_ATK
pub struct SkillChainLightningAttack;

impl SkillImpl for SkillChainLightningAttack {
    fn skill_id(&self) -> u16 {
        WL_CHAINLIGHTNING_ATK
    }

    /// SkillChainLightningAttack::calculateSkillRatio: `mflag` is the timerskill's `9 - skl->type` (8 on the first
    /// bolt, one less for each bounce).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, mflag: i32) {
        *skillratio += 400 + 100 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        if mflag > 0 {
            *skillratio += 100 * mflag;
        }
    }
}
