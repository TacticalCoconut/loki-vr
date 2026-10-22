//! skills/merchant/attackmachine.cpp: class SkillAttackMachine : public SkillImplRecursiveDamageSplash (MT_A_MACHINE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::Ctx;

/// SkillAttackMachine::SkillAttackMachine() : SkillImplRecursiveDamageSplash(MT_A_MACHINE).
pub struct SkillAttackMachine;

impl SkillImpl for SkillAttackMachine {
    fn skill_id(&self) -> u16 {
        MT_A_MACHINE
    }
    crate::splash_skill_defaults!();

    /// SkillAttackMachine::calculateSkillRatio: 150 + 700 * lv %, + 5 * POW ("TODO : unknown pow ratio"),
    /// RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -100 + 150 + 700 * skill_lv as i32;
        *base_skillratio += 5 * sstatus.pow as i32; // TODO : unknown pow ratio
        re_lvl_dmod(c, src, base_skillratio, 100);
    }
}

impl crate::skills::RecursiveDamageSplash for SkillAttackMachine {}
