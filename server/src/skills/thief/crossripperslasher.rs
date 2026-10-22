//! skills/thief/crossripperslasher.cpp: class SkillCrossRipperSlasher : public WeaponSkillImpl
//! (GC_CROSSRIPPERSLASHER).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_fail;
use crate::skill::clif::fail::USESKILL_FAIL_CONDITION;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_ROLLINGCUTTER;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillCrossRipperSlasher;

impl SkillImpl for SkillCrossRipperSlasher {
    fn skill_id(&self) -> u16 {
        GC_CROSSRIPPERSLASHER
    }

    /// SkillCrossRipperSlasher::calculateSkillRatio: + 200% per Rolling Cutter counter.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 80 * skill_lv as i32 + sstatus.agi as i32 * 3;
        re_lvl_dmod(c, src, skillratio, 100);
        if let Some(e) = sce(c, src, SC_ROLLINGCUTTER) {
            *skillratio += e.val1 * 200;
        }
    }

    /// SkillCrossRipperSlasher::castendDamageId: a player needs SC_ROLLINGCUTTER.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if src.kind == BL_PC && sce(c, src, SC_ROLLINGCUTTER).is_none() {
            clif_skill_fail(c, src, id, USESKILL_FAIL_CONDITION, 0, 0);
        } else {
            weapon_castend_damage_id(c, id, src, target, skill_lv, tick, *flag);
        }
    }
}
