//! skills/mage/frostdiver.cpp: class SkillFrostDiver : public SkillImpl (MG_FROSTDIVER).

use crate::battle::calc::Damage;
use crate::battle::BF_MAGIC;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::attack::skill_attack;
use crate::skill::clif::skill_fail;
use crate::skills::SkillImpl;
use crate::status::consts::SC_FREEZE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFrostDiver;

impl SkillImpl for SkillFrostDiver {
    fn skill_id(&self) -> u16 {
        MG_FROSTDIVER
    }

    /// SkillFrostDiver::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 10 * skill_lv as i32;
    }

    /// SkillFrostDiver::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }

    /// SkillFrostDiver::applyAdditionalEffects: SC_FREEZE; a player whose freeze did not start gets "skill failed"
    /// (clif_skill_fail with USESKILL_FAIL_LEVEL).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        let lv = skill_lv as i32;
        if !sc::sc_start(c, Some(src), target, SC_FREEZE, (lv * 3 + 35).min(lv + 60), lv, skill_get_time2(id, skill_lv) as i64) && src.kind == BL_PC {
            skill_fail(c, src, id);
        }
    }
}
