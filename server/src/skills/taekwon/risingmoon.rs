//! skills/taekwon/risingmoon.cpp: class SkillRisingMoon : public SkillImplRecursiveDamageSplash (SKE_RISING_MOON).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_DAWN_MOON, SC_MIDNIGHT_MOON, SC_RISING_MOON, SC_RISING_SUN};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillRisingMoon;

impl SkillImpl for SkillRisingMoon {
    fn skill_id(&self) -> u16 {
        SKE_RISING_MOON
    }

    crate::splash_skill_defaults!();

    /// SkillRisingMoon::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let lv = skill_lv as i32;
        let time = skill_get_time(id, skill_lv) as i64;
        // status_change* sc = status_get_sc(src); `sc == nullptr` answers None for every entry, which takes the first
        // branch as rAthena's `sc == nullptr ||` does
        let has = |t: i32| sce(c, src, t).is_some();
        if !has(SC_RISING_MOON) && !has(SC_MIDNIGHT_MOON) && !has(SC_DAWN_MOON) {
            sc::sc_start(c, Some(src), src, SC_RISING_MOON, 100, lv, time);
        } else if !has(SC_MIDNIGHT_MOON) && !has(SC_DAWN_MOON) {
            sc::sc_start(c, Some(src), src, SC_MIDNIGHT_MOON, 100, lv, time);
        } else if !has(SC_DAWN_MOON) {
            sc::sc_start(c, Some(src), src, SC_DAWN_MOON, 100, lv, time);
        } else if has(SC_RISING_SUN) {
            // (rAthena ends SC_DAWN_MOON on the target, not on the caster whose status it checked)
            sc::status_change_end(c, target.id, SC_DAWN_MOON, 0);
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&s), &t, id, lv, true);
        skill_castend_damage_id(c, &s, &t, id, skill_lv, tick, *flag);
    }

    /// SkillRisingMoon::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 700 + 450 * lv;
        *skillratio += pc_checkskill(c, src, SKE_SKY_MASTERY) * 5 * lv;
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillRisingMoon {}
