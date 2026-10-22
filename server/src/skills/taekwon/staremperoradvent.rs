//! skills/taekwon/staremperoradvent.cpp: class SkillStarEmperorAdvent : public SkillImplRecursiveDamageSplash (SJ_STAREMPEROR).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::sce;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_DIMENSION, SC_DIMENSION1, SC_DIMENSION2, SC_SILENCE};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillStarEmperorAdvent;

impl SkillImpl for SkillStarEmperorAdvent {
    fn skill_id(&self) -> u16 {
        SJ_STAREMPEROR
    }

    crate::splash_skill_defaults!();

    /// SkillStarEmperorAdvent::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let lv = skill_lv as i32;
        sc::sc_start(c, Some(src), target, SC_SILENCE, 50 + 10 * lv, lv, skill_get_time(self.skill_id(), skill_lv) as i64);
    }

    /// SkillStarEmperorAdvent::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 700 + 200 * skill_lv as i32;
    }

    /// SkillStarEmperorAdvent::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if sce(c, src, SC_DIMENSION).is_some() {
            if src.kind == BL_PC {
                // Remove old shields if any exist.
                let spiritball = crate::skill::state::get(c, src.id).spiritball as i32;
                crate::skill::state::pc_delspiritball(c, src.id, spiritball, 0);
                // (both on the target, as rAthena does; status_get_max_sp(src) read at each call)
                let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
                let max_sp = crate::status::status_get_status_data(c, src).max_sp as i32;
                sc::sc_start2(c, Some(src), &t, SC_DIMENSION1, 100, skill_lv as i32, max_sp, skill_get_time2(SJ_BOOKOFDIMENSION, 1) as i64);
                let t = crate::unit::get(c, target.id).unwrap_or_else(|| t.clone());
                let max_sp = crate::status::status_get_status_data(c, src).max_sp as i32;
                sc::sc_start2(c, Some(src), &t, SC_DIMENSION2, 100, skill_lv as i32, max_sp, skill_get_time2(SJ_BOOKOFDIMENSION, 1) as i64);
            }
            sc::status_change_end(c, src.id, SC_DIMENSION, 0);
        }

        let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true);
        skill_castend_damage_id(c, &s, &t, id, skill_lv, tick, *flag);
    }
}

impl RecursiveDamageSplash for SkillStarEmperorAdvent {}
