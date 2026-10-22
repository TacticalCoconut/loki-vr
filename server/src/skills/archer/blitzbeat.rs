//! skills/archer/blitzbeat.cpp: class SkillBlitzBeat : public SkillImplRecursiveDamageSplash (HT_BLITZBEAT). It
//! overrides nothing: the splash around the target, each hit a skill_attack of the skill's type (its damage is
//! battle_calc_misc_attack's HT_BLITZBEAT case; the automatic Blitz Beat is skill_additional_effect's).

use crate::db::skills::*;
use crate::skills::{RecursiveDamageSplash, SkillImpl};

pub struct SkillBlitzBeat;

impl SkillImpl for SkillBlitzBeat {
    fn skill_id(&self) -> u16 {
        HT_BLITZBEAT
    }

    crate::splash_skill_defaults!();
}

impl RecursiveDamageSplash for SkillBlitzBeat {}
