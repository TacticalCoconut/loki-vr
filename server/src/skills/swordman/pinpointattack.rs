//! skills/swordman/pinpointattack.cpp: class SkillPinpointAttack : public WeaponSkillImpl (LG_PINPOINTATTACK).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::inventory::{EQP_ARMOR, EQP_HELM, EQP_SHIELD, EQP_WEAPON};
use crate::map::BL_PC;
use crate::skill::effect::skill_break_equip;
use crate::skill::misc::skill_check_unit_movepos;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_BLEEDING;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillPinpointAttack;

impl SkillImpl for SkillPinpointAttack {
    fn skill_id(&self) -> u16 {
        LG_PINPOINTATTACK
    }

    /// SkillPinpointAttack::castendDamageId: the caster moves onto the target's cell, then the hit.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        if skill_check_unit_movepos(c, 5, src, target.x, target.y, 1, true) {
            if let Some(s) = unit::get(c, src.id) {
                crate::battle::clif::clif_blown(c, &s);
            }
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        weapon_castend_damage_id(c, self.skill_id(), &s, target, skill_lv, tick, *flag);
    }

    /// SkillPinpointAttack::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let agi = crate::status::status_get_status_data(c, src).agi as i32;
        *skillratio += -100 + 100 * skill_lv as i32 + 5 * agi;
        re_lvl_dmod(c, src, skillratio, 120);
    }

    /// SkillPinpointAttack::applyAdditionalEffects: bleeding (level 1) or an equipment break (levels 2 to 5).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        let agi = crate::status::status_get_status_data(c, src).agi as i32;
        // ((sd) ? pc_checkskill(sd, getSkillId()) : skill_lv)
        let lv = if src.kind == BL_PC { pc_checkskill(c, src, id) } else { skill_lv as i32 };
        let rate = 30 + 5 * lv + (agi + crate::status::status_get_lv(c, src)) / 10;
        match skill_lv {
            1 => {
                sc::sc_start2(c, Some(src), target, SC_BLEEDING, rate, skill_lv as i32, src.id as i32, skill_get_time(id, skill_lv) as i64);
            }
            2 => {
                skill_break_equip(c, src, target, EQP_HELM as u16, rate * 100, BCT_ENEMY);
            }
            3 => {
                skill_break_equip(c, src, target, EQP_SHIELD as u16, rate * 100, BCT_ENEMY);
            }
            4 => {
                skill_break_equip(c, src, target, EQP_ARMOR as u16, rate * 100, BCT_ENEMY);
            }
            5 => {
                skill_break_equip(c, src, target, EQP_WEAPON as u16, rate * 100, BCT_ENEMY);
            }
            _ => {}
        }
    }
}
