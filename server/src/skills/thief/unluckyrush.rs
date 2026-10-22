//! skills/thief/unluckyrush.cpp: class SkillUnluckyRush : public WeaponSkillImpl (ABC_UNLUCKY_RUSH).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::attack::{skill_blown, BLOWN_NONE};
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::misc::skill_check_unit_movepos;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::{SC_CHASING, SC_HANDICAPSTATE_MISFORTUNE};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillUnluckyRush;

impl SkillImpl for SkillUnluckyRush {
    fn skill_id(&self) -> u16 {
        ABC_UNLUCKY_RUSH
    }

    /// SkillUnluckyRush::castendDamageId: jump to the target before attacking (onto its cell, then one cell back
    /// towards where the caster came from), the animation, then the hit.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // Jump to the target before attacking.
        if skill_check_unit_movepos(c, 5, src, target.x, target.y, 0, true) {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let dir = (crate::map::calc_dir(target, s.x, s.y) + 4) % 8;
            skill_blown(c, &s, &s, 1, dir as i8, BLOWN_NONE);
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        clif_skill_nodamage(c, Some(&s), target, id, skill_lv as i32, true);
        weapon_castend_damage_id(c, id, &s, target, skill_lv, tick, *flag);
    }

    /// SkillUnluckyRush::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 100 + 300 * lv + 5 * sstatus.pow as i32;
        if sce(c, src, SC_CHASING).is_some() {
            *skillratio += 2500 * lv;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillUnluckyRush::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_HANDICAPSTATE_MISFORTUNE, 30 + 10 * skill_lv as i32, skill_lv as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
    }
}
