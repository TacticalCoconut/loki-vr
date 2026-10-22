//! skills/gunslinger/shatterstorm.cpp: class SkillShatterStorm : public SkillImplRecursiveDamageSplash (RL_S_STORM),
//! rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::inventory::EQP_HEAD_TOP;
use crate::skill::effect::skill_break_equip;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillShatterStorm;

impl SkillImpl for SkillShatterStorm {
    fn skill_id(&self) -> u16 {
        RL_S_STORM
    }

    crate::splash_skill_defaults!();

    /// SkillShatterStorm::applyAdditionalEffects: kRO update 2014-02-12: break a headgear by minimum chance
    /// 5%/10%/15%/20%/25% (max(level x 500, DEX x level x 10 - AGI x 20) of 10000; TODO in rAthena: figure out the
    /// break chance formula).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let tstatus = crate::status::status_get_status_data(c, target);
        let lv = skill_lv as i32;
        let rate = (lv * 500).max(sstatus.dex as i32 * lv * 10 - tstatus.agi as i32 * 20);
        skill_break_equip(c, src, target, EQP_HEAD_TOP as u16, rate, BCT_ENEMY);
    }

    /// SkillShatterStorm::calculateSkillRatio: 1700 + 200% per level.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -100 + 1700 + 200 * skill_lv as i32;
    }
}

impl RecursiveDamageSplash for SkillShatterStorm {}
