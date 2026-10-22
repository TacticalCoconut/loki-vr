//! skills/summoner/pickypeck.cpp: class SkillPickyPeck : public WeaponSkillImpl (SU_PICKYPECK) and class
//! SkillPickyPeckDoubleAttack : public SkillImpl (SU_PICKYPECK_DOUBLE_ATK; rAthena's "FIX ME: this skill is never
//! triggered").

use super::add_hp_ratio;
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::pc_checkskill;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// The calculateSkillRatio both classes have (the same body): doubled against a target under half its HP
/// (`status_get_hp(target) < status_get_max_hp(target) / 2`, uint32), then the Spirit of Life bonus.
fn picky_ratio(c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, base_skillratio: &mut i32) {
    *base_skillratio += 100 + 100 * skill_lv as i32;
    let tstatus = crate::status::status_get_status_data(c, target);
    if tstatus.hp < tstatus.max_hp / 2 {
        *base_skillratio *= 2;
    }
    if src.kind == BL_PC && pc_checkskill(c, src, SU_SPIRITOFLIFE) != 0 {
        add_hp_ratio(c, src, base_skillratio);
    }
}

// SU_PICKYPECK
pub struct SkillPickyPeck;

impl SkillImpl for SkillPickyPeck {
    fn skill_id(&self) -> u16 {
        SU_PICKYPECK
    }

    /// SkillPickyPeck::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        picky_ratio(c, src, target, skill_lv, base_skillratio);
    }

    /// SkillPickyPeck::castendDamageId: the skill packet, then WeaponSkillImpl::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }
}

// SU_PICKYPECK_DOUBLE_ATK
pub struct SkillPickyPeckDoubleAttack;

impl SkillImpl for SkillPickyPeckDoubleAttack {
    fn skill_id(&self) -> u16 {
        SU_PICKYPECK_DOUBLE_ATK
    }

    /// SkillPickyPeckDoubleAttack::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        picky_ratio(c, src, target, skill_lv, base_skillratio);
    }
}
