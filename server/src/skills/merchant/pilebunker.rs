//! skills/merchant/pilebunker.cpp: class SkillPileBunker : public WeaponSkillImpl (NC_PILEBUNKER).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::status::consts::*;
use crate::status::sc;
use crate::status::status_get_status_data;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillPileBunker::SkillPileBunker() : WeaponSkillImpl(NC_PILEBUNKER).
pub struct SkillPileBunker;

impl SkillImpl for SkillPileBunker {
    fn skill_id(&self) -> u16 {
        NC_PILEBUNKER
    }

    /// WeaponSkillImpl::castendDamageId (inherited).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        crate::skills::weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillPileBunker::applyAdditionalEffects: with a 25% + 15% per level chance, the target's defensive statuses end.
    fn apply_additional_effects(&self, c: &Ctx, _src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        if ((c.rnd() % 100) as i32) < 25 + 15 * skill_lv as i32 {
            for t in [SC_KYRIE, SC_ASSUMPTIO, SC_STEELBODY, SC_GT_CHANGE, SC_GT_REVITALIZE, SC_AUTOGUARD, SC_REFLECTDAMAGE, SC_DEFENDER, SC_PRESTIGE, SC_BANDING, SC_MILLENNIUMSHIELD] {
                sc::status_change_end(c, target.id, t, 0);
            }
        }
    }

    /// SkillPileBunker::calculateSkillRatio: +200% + 100% per level + STR, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 200 + 100 * skill_lv as i32 + status_get_status_data(c, src).str as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
