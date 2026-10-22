//! skills/mage/lordofvermilion.cpp: class SkillLordOfVermilion : public SkillImpl (WZ_VERMILION). The hits come
//! from its skill unit (skillunit/ontimer.rs).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::iface;
use crate::skills::SkillImpl;
use crate::status::consts::SC_BLIND;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillLordOfVermilion;

impl SkillImpl for SkillLordOfVermilion {
    fn skill_id(&self) -> u16 {
        WZ_VERMILION
    }

    /// SkillLordOfVermilion::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        //Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        *flag |= 1;
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }

    /// SkillLordOfVermilion::calculateSkillRatio (RENEWAL branch).
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let lv = skill_lv as i32;
        // const map_session_data* sd = BL_CAST(BL_PC, src)
        if src.kind == BL_PC {
            *base_skillratio += 300 + lv * 100;
        } else {
            *base_skillratio += 20 * lv - 20; //Monsters use old formula
        }
    }

    /// SkillLordOfVermilion::applyAdditionalEffects (RENEWAL branch).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let lv = skill_lv as i32;
        sc::sc_start(c, Some(src), target, SC_BLIND, 10 + 5 * lv, lv, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }
}
