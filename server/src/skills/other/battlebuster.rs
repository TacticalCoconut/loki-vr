//! skills/other/battlebuster.cpp: class SkillBattleBuster : public WeaponSkillImpl (ABR_BATTLE_BUSTER).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillBattleBuster;

impl SkillImpl for SkillBattleBuster {
    fn skill_id(&self) -> u16 {
        ABR_BATTLE_BUSTER
    }

    /// WeaponSkillImpl::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillBattleBuster::calculateSkillRatio ("TODO: Need official formula.").
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -100 + 8000;
    }
}
