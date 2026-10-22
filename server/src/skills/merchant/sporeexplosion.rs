//! skills/merchant/sporeexplosion.cpp: class SkillSporeExplosion : public SkillImplRecursiveDamageSplash (GN_SPORE_EXPLOSION).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_BIONIC_WOODEN_FAIRY, SC_SPORE_EXPLOSION};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillSporeExplosion::SkillSporeExplosion() : SkillImplRecursiveDamageSplash(GN_SPORE_EXPLOSION).
pub struct SkillSporeExplosion;

impl SkillImpl for SkillSporeExplosion {
    fn skill_id(&self) -> u16 {
        GN_SPORE_EXPLOSION
    }
    crate::splash_skill_defaults!();

    /// SkillSporeExplosion::applyAdditionalEffects: SC_SPORE_EXPLOSION.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_SPORE_EXPLOSION, 100, skill_lv as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
    }

    /// SkillSporeExplosion::calculateSkillRatio: 400% + 200% per level, RE_LVL_DMOD(100), doubled under
    /// SC_BIONIC_WOODEN_FAIRY.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 400 + 200 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        if sce(c, src, SC_BIONIC_WOODEN_FAIRY).is_some() {
            *skillratio *= 2;
        }
    }
}

impl RecursiveDamageSplash for SkillSporeExplosion {}
