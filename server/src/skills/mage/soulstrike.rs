//! skills/mage/soulstrike.cpp: class SkillSoulStrike : public SkillImpl (MG_SOULSTRIKE).

use crate::battle::calc::Damage;
use crate::battle::BF_MAGIC;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skills::SkillImpl;
use crate::status::sc::battle_check_undead;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSoulStrike;

impl SkillImpl for SkillSoulStrike {
    fn skill_id(&self) -> u16 {
        MG_SOULSTRIKE
    }

    /// SkillSoulStrike::calculateSkillRatio: +5% per level against undead.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, _src: &Unit, target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let tstatus = crate::status::status_get_status_data(c, target);
        if battle_check_undead(tstatus.race as i32, tstatus.def_ele as i32) {
            *base_skillratio += 5 * skill_lv as i32;
        }
    }

    /// SkillSoulStrike::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }
}
