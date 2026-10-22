//! skills/merchant/powerswing.cpp: class SkillPowerSwing : public WeaponSkillImpl (NC_POWERSWING).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::SkillImpl;
use crate::status::consts::{SC_ABR_BATTLE_WARIOR, SC_STUN};
use crate::status::sc;
use crate::status::status_get_status_data;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillPowerSwing::SkillPowerSwing() : WeaponSkillImpl(NC_POWERSWING).
pub struct SkillPowerSwing;

impl SkillImpl for SkillPowerSwing {
    fn skill_id(&self) -> u16 {
        NC_POWERSWING
    }

    /// WeaponSkillImpl::castendDamageId (inherited).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        crate::skills::weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// modifyDamageData: `dmg.div_ = -2` while the caster has SC_ABR_BATTLE_WARIOR.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if crate::skills::novice::sce(c, src, SC_ABR_BATTLE_WARIOR).is_some() {
            dmg.div_ = -2;
        }
    }

    /// SkillPowerSwing::applyAdditionalEffects: SC_STUN at 10%.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_STUN, 10, skill_lv as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
    }

    /// SkillPowerSwing::calculateSkillRatio: (STR + DEX) / 2 + 300% + 100% per level, RE_LVL_DMOD(100) ("only the str
    /// + dex gets modified by level [Akinari]"), doubled under SC_ABR_BATTLE_WARIOR.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = status_get_status_data(c, src);
        *skillratio += -100 + ((sstatus.str as i32 + sstatus.dex as i32) / 2) + 300 + 100 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        if sce(c, src, SC_ABR_BATTLE_WARIOR).is_some() {
            *skillratio *= 2;
        }
    }
}
