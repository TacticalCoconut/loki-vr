//! skills/merchant/hellsplant.cpp: class SkillHellsPlant : public StatusSkillImpl (GN_HELLS_PLANT); class
//! SkillHellsPlantAttack : public SkillImplRecursiveDamageSplash (GN_HELLS_PLANT_ATK, the hits SC_HELLS_PLANT's timer
//! makes: status_change_timer calls skill_castend_damage_id).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_BLEEDING, SC_STUN};
use crate::status::sc;
use crate::status::status_get_status_data;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillHellsPlant::SkillHellsPlant() : StatusSkillImpl(GN_HELLS_PLANT) (no override).
pub struct SkillHellsPlant;

impl SkillImpl for SkillHellsPlant {
    fn skill_id(&self) -> u16 {
        GN_HELLS_PLANT
    }

    /// StatusSkillImpl::castendNoDamageId (inherited).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        crate::skills::status_castend_nodamage_id(c, self.skill_id(), false, src, target, skill_lv);
    }
}

/// SkillHellsPlantAttack::SkillHellsPlantAttack() : SkillImplRecursiveDamageSplash(GN_HELLS_PLANT_ATK).
pub struct SkillHellsPlantAttack;

impl SkillImpl for SkillHellsPlantAttack {
    fn skill_id(&self) -> u16 {
        GN_HELLS_PLANT_ATK
    }
    crate::splash_skill_defaults!();

    /// SkillHellsPlantAttack::applyAdditionalEffects: SC_STUN (20% + 10% per level) and SC_BLEEDING (5% + 5% per level).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        let t = skill_get_time(id, skill_lv) as i64;
        sc::sc_start(c, Some(src), target, SC_STUN, 20 + 10 * skill_lv as i32, skill_lv as i32, t);
        let tg = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        sc::sc_start2(c, Some(src), &tg, SC_BLEEDING, 5 + 5 * skill_lv as i32, skill_lv as i32, src.id as i32, t);
    }

    /// SkillHellsPlantAttack::calculateSkillRatio: 100% per level + INT x Cannibalize level (5 for non-players) ("!TODO:
    /// Confirm INT and Cannibalize bonus"), RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let int_ = status_get_status_data(c, src).int_ as i32;
        let mult = if src.kind == BL_PC { pc_checkskill(c, src, AM_CANNIBALIZE) } else { 5 };
        *skillratio += -100 + 100 * skill_lv as i32 + int_ * mult;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillHellsPlantAttack {}
