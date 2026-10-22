//! skills/acolyte/glacierfist.cpp: class SkillGlacierFist : public WeaponSkillImpl (CH_TIGERFIST).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::{SC_ANKLE, SC_GT_ENERGYGAIN};
use crate::status::defs::MD_STATUSIMMUNE;
use crate::status::{sc, status_get_lv};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGlacierFist;

impl SkillImpl for SkillGlacierFist {
    fn skill_id(&self) -> u16 {
        CH_TIGERFIST
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillGlacierFist::calculateSkillRatio (RENEWAL): +400% + 150% per level (RE_LVL_DMOD 100); +50% under Gentle
    /// Touch - Energy Gain.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 400 + 150 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        if sce(c, src, SC_GT_ENERGYGAIN).is_some() {
            *skillratio += *skillratio * 50 / 100;
        }
    }

    /// SkillGlacierFist::applyAdditionalEffects: the ankle hold, shorter for status-immune targets (1/5) and by the
    /// target's AGI, at least 15 * (caster level + 100) ms.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let mut basetime = skill_get_time(self.skill_id(), skill_lv) as i64;
        let mintime = 15 * (status_get_lv(c, src) as i64 + 100);
        if crate::battle::bl::has_mode(c, target, MD_STATUSIMMUNE) {
            basetime /= 5;
        }
        let agi = crate::status::status_get_status_data(c, target).agi as i64;
        basetime = ((basetime * agi) / -200 + basetime).max(mintime);
        sc::sc_start(c, Some(src), target, SC_ANKLE, (1 + skill_lv as i32) * 10, 0, basetime);
    }
}
