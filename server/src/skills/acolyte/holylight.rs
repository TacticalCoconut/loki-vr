//! skills/acolyte/holylight.cpp: class SkillHolyLight : public SkillImpl (AL_HOLYLIGHT).

use crate::battle::calc::Damage;
use crate::battle::BF_MAGIC;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::attack::skill_attack;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_P_ALTER, SC_SPIRIT};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillHolyLight;

impl SkillImpl for SkillHolyLight {
    fn skill_id(&self) -> u16 {
        AL_HOLYLIGHT
    }

    /// SkillHolyLight::castendDamageId: ends the target's Platinum Altar, then the magic hit.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        sc::status_change_end(c, target.id, SC_P_ALTER, 0);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        skill_attack(c, BF_MAGIC, src, src, &t, self.skill_id(), skill_lv, tick, *flag);
    }

    /// SkillHolyLight::calculateSkillRatio: +25%; x5 under the Priest spirit (Soul Link).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 25;
        // sd && sd->sc.getSCE(SC_SPIRIT) && sd->sc.getSCE(SC_SPIRIT)->val2 == SL_PRIEST
        if src.kind == BL_PC && sce(c, src, SC_SPIRIT).is_some_and(|e| e.val2 == SL_PRIEST as i32) {
            *base_skillratio *= 5; // Does 5x damage include bonuses from other skills?
        }
    }
}
