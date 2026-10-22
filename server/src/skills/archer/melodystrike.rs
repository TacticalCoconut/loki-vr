//! skills/archer/melodystrike.cpp: class SkillMelodyStrike : public WeaponSkillImpl (BA_MUSICALSTRIKE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMelodyStrike;

impl SkillImpl for SkillMelodyStrike {
    fn skill_id(&self) -> u16 {
        BA_MUSICALSTRIKE
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillMelodyStrike::calculateSkillRatio: Renewal: `base_skillratio += 10 + 40 * skill_lv`.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 10 + 40 * skill_lv as i32;
    }
}
