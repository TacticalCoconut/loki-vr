//! skills/thief/grimtooth.cpp: class SkillGrimtooth : public SkillImplRecursiveDamageSplash (AS_GRIMTOOTH).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_MOB;
use crate::skill::SD_PREAMBLE;
use crate::skills::{splash_castend_damage_id, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_QUAGMIRE;
use crate::status::defs::MD_STATUSIMMUNE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGrimtooth;

impl SkillImpl for SkillGrimtooth {
    fn skill_id(&self) -> u16 {
        AS_GRIMTOOTH
    }

    /// SkillGrimtooth::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 20 * skill_lv as i32;
    }

    /// SkillGrimtooth::castendDamageId: a fake packet will be sent for the first target to be hit (SD_PREAMBLE),
    /// then the recursive splash.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        *flag |= SD_PREAMBLE;
        splash_castend_damage_id(self, c, src, target, skill_lv, tick, flag);
    }

    /// SkillImplRecursiveDamageSplash::castendPos2 (not overridden).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        crate::skills::splash_castend_pos2(self, c, src, x, y, skill_lv, tick, flag);
    }

    /// SkillGrimtooth::applyAdditionalEffects: Quagmire on a monster without status immunity.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let tstatus = crate::status::status_get_status_data(c, target);
        if target.kind == BL_MOB && tstatus.mode & MD_STATUSIMMUNE == 0 {
            sc::sc_start(c, Some(src), target, SC_QUAGMIRE, 100, 0, skill_get_time2(self.skill_id(), skill_lv) as i64);
        }
    }
}

impl RecursiveDamageSplash for SkillGrimtooth {}
