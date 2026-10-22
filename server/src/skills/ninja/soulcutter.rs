//! skills/ninja/soulcutter.cpp: class SkillSoulCutter : public WeaponSkillImpl (KO_SETSUDAN), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::{SC_SOULFAIRY, SC_SOULFALCON, SC_SOULGOLEM, SC_SOULSHADOW, SC_SPIRIT};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSoulCutter;

impl SkillImpl for SkillSoulCutter {
    fn skill_id(&self) -> u16 {
        KO_SETSUDAN
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillSoulCutter::applyAdditionalEffects: remove soul link when hit.
    fn apply_additional_effects(&self, c: &Ctx, _src: &Unit, target: &Unit, _skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        for t in [SC_SPIRIT, SC_SOULGOLEM, SC_SOULSHADOW, SC_SOULFALCON, SC_SOULFAIRY] {
            sc::status_change_end(c, target.id, t, 0);
        }
    }

    /// SkillSoulCutter::calculateSkillRatio: 100% per level above 1, RE_LVL_DMOD(100), + 200% per level of the first
    /// soul link found (Spirit, Golem, Shadow, Falcon, Fairy: bonus damage added when target is soul linked).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 100 * (skill_lv as i32 - 1);
        re_lvl_dmod(c, src, skillratio, 100);
        if let Some(e) = [SC_SPIRIT, SC_SOULGOLEM, SC_SOULSHADOW, SC_SOULFALCON, SC_SOULFAIRY].into_iter().find_map(|t| sce(c, target, t)) {
            *skillratio += 200 * e.val1;
        }
    }
}
