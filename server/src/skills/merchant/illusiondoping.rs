//! skills/merchant/illusiondoping.cpp: class SkillIllusionDoping : public SkillImplRecursiveDamageSplash (GN_ILLUSIONDOPING).

use crate::db::skills::*;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_HALLUCINATION, SC_ILLUSIONDOPING};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillIllusionDoping::SkillIllusionDoping() : SkillImplRecursiveDamageSplash(GN_ILLUSIONDOPING).
pub struct SkillIllusionDoping;

impl SkillImpl for SkillIllusionDoping {
    fn skill_id(&self) -> u16 {
        GN_ILLUSIONDOPING
    }
    crate::splash_skill_defaults!();

    /// SkillIllusionDoping::applyAdditionalEffects: SC_ILLUSIONDOPING (100% - 10% per level), and when it started,
    /// SC_HALLUCINATION.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        let t = skill_get_time(id, skill_lv) as i64;
        if sc::sc_start(c, Some(src), target, SC_ILLUSIONDOPING, 100 - skill_lv as i32 * 10, skill_lv as i32, t) {
            let tg = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            sc::sc_start(c, Some(src), &tg, SC_HALLUCINATION, 100, skill_lv as i32, t);
        }
    }
}

impl RecursiveDamageSplash for SkillIllusionDoping {}
