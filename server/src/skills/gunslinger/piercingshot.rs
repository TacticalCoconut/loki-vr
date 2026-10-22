//! skills/gunslinger/piercingshot.cpp: class SkillPiercingShot : public WeaponSkillImpl (GS_PIERCINGSHOT), rAthena
//! e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::inventory::W_RIFLE;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_BLEEDING;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillPiercingShot;

impl SkillImpl for SkillPiercingShot {
    fn skill_id(&self) -> u16 {
        GS_PIERCINGSHOT
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillPiercingShot::calculateSkillRatio (RENEWAL): + 150 + 30% per level with a rifle, else + 100 + 20% per
    /// level.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        if super::weapontype1(c, src) == Some(W_RIFLE) {
            *base_skillratio += 150 + 30 * skill_lv as i32;
        } else {
            *base_skillratio += 100 + 20 * skill_lv as i32;
        }
    }

    /// SkillPiercingShot::applyAdditionalEffects: bleeding, 3% per level.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start2(c, Some(src), target, SC_BLEEDING, skill_lv as i32 * 3, skill_lv as i32, src.id as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }
}
