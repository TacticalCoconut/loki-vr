//! skills/taekwon/risingsun.cpp: class SkillRisingSun : public SkillImpl (SKE_RISING_SUN).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::SkillImpl;
use crate::status::consts::{SC_NOON_SUN, SC_RISING_SUN, SC_SUNSET_SUN};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillRisingSun;

impl SkillImpl for SkillRisingSun {
    fn skill_id(&self) -> u16 {
        SKE_RISING_SUN
    }

    /// SkillRisingSun::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let lv = skill_lv as i32;
        clif_skill_nodamage(c, Some(src), target, id, lv, true);
        skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
        // status_change* sc = status_get_sc(src) (read before the attack; the entries are read now, as rAthena's
        // pointer does). `sc == nullptr` answers None for every entry, which takes the first branch.
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let time = skill_get_time(id, skill_lv) as i64;
        let has = |t: i32| sce(c, &s, t).is_some();
        if !has(SC_RISING_SUN) && !has(SC_NOON_SUN) && !has(SC_SUNSET_SUN) {
            sc::sc_start(c, Some(&s), &s, SC_RISING_SUN, 100, lv, time);
        } else if !has(SC_NOON_SUN) && !has(SC_SUNSET_SUN) {
            sc::sc_start(c, Some(&s), &s, SC_NOON_SUN, 100, lv, time);
        } else if !has(SC_SUNSET_SUN) {
            sc::sc_start(c, Some(&s), &s, SC_SUNSET_SUN, 100, lv, time);
        }
    }

    /// SkillRisingSun::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 500 + 600 * lv;
        *skillratio += pc_checkskill(c, src, SKE_SKY_MASTERY) * 5 * lv;
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
