//! skills/thief/meteorassault.cpp: class SkillMeteorAssault : public SkillImplRecursiveDamageSplash (ASC_METEORASSAULT).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_BLEEDING, SC_BLIND, SC_STUN};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillMeteorAssault;

impl SkillImpl for SkillMeteorAssault {
    fn skill_id(&self) -> u16 {
        ASC_METEORASSAULT
    }

    crate::splash_skill_defaults!();

    /// SkillMeteorAssault::calculateSkillRatio (RENEWAL).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 100 + 120 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillMeteorAssault::castendNoDamageId: the animation, then the damage part.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        skill_castend_damage_id(c, &s, &t, id, skill_lv, tick, *flag);
    }

    /// SkillMeteorAssault::applyAdditionalEffects: any enemies hit by this skill will receive Stun, Darkness, or
    /// external bleeding status ailment with a 5%+5*skill_lv% chance.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        let lv = skill_lv as i32;
        match c.rnd() % 3 {
            0 => {
                sc::sc_start(c, Some(src), target, SC_BLIND, 5 + lv * 5, lv, skill_get_time2(id, 1) as i64);
            }
            1 => {
                sc::sc_start(c, Some(src), target, SC_STUN, 5 + lv * 5, lv, skill_get_time2(id, 2) as i64);
            }
            _ => {
                sc::sc_start2(c, Some(src), target, SC_BLEEDING, 5 + lv * 5, lv, src.id as i32, skill_get_time2(id, 3) as i64);
            }
        }
    }
}

impl RecursiveDamageSplash for SkillMeteorAssault {}
