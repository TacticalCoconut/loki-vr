//! skills/merchant/demonicfire.cpp: class SkillDemonicFire : public SkillImplRecursiveDamageSplash (GN_DEMONIC_FIRE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::status_get_status_data;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillDemonicFire::SkillDemonicFire() : SkillImplRecursiveDamageSplash(GN_DEMONIC_FIRE).
pub struct SkillDemonicFire;

impl SkillImpl for SkillDemonicFire {
    fn skill_id(&self) -> u16 {
        GN_DEMONIC_FIRE
    }

    /// SkillImplRecursiveDamageSplash::castendDamageId (inherited).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        crate::skills::splash_castend_damage_id(self, c, src, target, skill_lv, tick, flag);
    }

    /// SkillDemonicFire::calculateSkillRatio: levels above 20 are Fire Expansion level 2 (+10% + 20% per level + INT x
    /// 10), above 10 Fire Expansion level 1 (+ INT + job level (50 for non-players), RE_LVL_DMOD(100)), else +10% +
    /// 20% per level.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let lv = skill_lv as i32;
        let int_ = status_get_status_data(c, src).int_ as i32;
        if lv > 20 {
            // Fire expansion Lv.2
            *skillratio += 10 + 20 * (lv - 20) + int_ * 10;
        } else if lv > 10 {
            // Fire expansion Lv.1
            let job = if src.kind == BL_PC { crate::pc::character_of(c, src.id).map(|ch| ch.job_level as i32).unwrap_or(0) } else { 50 };
            *skillratio += 10 + 20 * (lv - 10) + int_ + job;
            re_lvl_dmod(c, src, skillratio, 100);
        } else {
            *skillratio += 10 + 20 * lv;
        }
    }

    /// SkillDemonicFire::castendPos2: the fire unit ("Ammo should be deleted right away": flag untouched).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        crate::skill::iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}

impl RecursiveDamageSplash for SkillDemonicFire {}
