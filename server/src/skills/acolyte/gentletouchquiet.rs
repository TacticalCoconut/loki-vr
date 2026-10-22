//! skills/acolyte/gentletouchquiet.cpp: class SkillGentleTouchQuiet : public WeaponSkillImpl (SR_GENTLETOUCH_QUIET).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_SILENCE;
use crate::status::{sc, status_get_lv};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGentleTouchQuiet;

impl SkillImpl for SkillGentleTouchQuiet {
    fn skill_id(&self) -> u16 {
        SR_GENTLETOUCH_QUIET
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillGentleTouchQuiet::applyAdditionalEffects: silence at [(Skill Level x 5) + (Caster's DEX + Caster's Base
    /// Level) / 10]%.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let dex = crate::status::status_get_status_data(c, src).dex as i32;
        let rate = 5 * skill_lv as i32 + (dex + status_get_lv(c, src)) / 10;
        sc::sc_start(c, Some(src), target, SC_SILENCE, rate, skill_lv as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
    }

    /// SkillGentleTouchQuiet::calculateSkillRatio: 100% per level + DEX (RE_LVL_DMOD 100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 100 * skill_lv as i32 + sstatus.dex as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
