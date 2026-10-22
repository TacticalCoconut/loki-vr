//! skills/taekwon/fullmoonkick.cpp: class SkillFullMoonKick : public SkillImplRecursiveDamageSplash (SJ_FULLMOONKICK).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_BLIND, SC_LIGHTOFMOON};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFullMoonKick;

impl SkillImpl for SkillFullMoonKick {
    fn skill_id(&self) -> u16 {
        SJ_FULLMOONKICK
    }

    crate::splash_skill_defaults!();

    /// SkillFullMoonKick::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let lv = skill_lv as i32;
        sc::sc_start(c, Some(src), target, SC_BLIND, 15 + 5 * lv, lv, skill_get_time(self.skill_id(), skill_lv) as i64);
    }

    /// SkillFullMoonKick::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 1000 + 100 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        if let Some(e) = sce(c, src, SC_LIGHTOFMOON) {
            *skillratio += *skillratio * e.val2 / 100;
        }
    }

    /// SkillFullMoonKick::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        skill_castend_damage_id(c, src, target, id, skill_lv, tick, *flag);
    }
}

impl RecursiveDamageSplash for SkillFullMoonKick {}
