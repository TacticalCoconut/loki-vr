//! skills/taekwon/prominencekick.cpp: class SkillProminenceKick : public SkillImplRecursiveDamageSplash (SJ_PROMINENCEKICK).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skill::SD_ANIMATION;
use crate::skills::novice::splash_damage_default;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::defs::ELE_FIRE;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillProminenceKick;

impl SkillImpl for SkillProminenceKick {
    fn skill_id(&self) -> u16 {
        SJ_PROMINENCEKICK
    }

    crate::splash_skill_defaults!();

    /// SkillProminenceKick::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 50 + 50 * skill_lv as i32;
    }

    /// SkillProminenceKick::modifyElement.
    fn modify_element(&self, _c: &Ctx, _dmg: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        *element = ELE_FIRE;
    }
}

impl RecursiveDamageSplash for SkillProminenceKick {
    /// SkillProminenceKick::splashDamage.
    fn splash_damage(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) -> i64 {
        let id = self.skill_id();
        let dmg = splash_damage_default(c, id, src, target, skill_lv, tick, flag);

        // Trigger the 2nd hit. (100% fire damage.)
        let s = crate::unit::get_or_freed(c, src.id).unwrap_or_else(|| src.clone());
        let t = crate::unit::get_or_freed(c, target.id).unwrap_or_else(|| target.clone());
        skill_attack(c, skill_get_type(id), &s, &s, &t, id, skill_lv, tick, flag | 8 | SD_ANIMATION);

        dmg
    }
}
