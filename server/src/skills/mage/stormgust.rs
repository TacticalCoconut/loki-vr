//! skills/mage/stormgust.cpp: class SkillStormGust : public SkillImpl (WZ_STORMGUST). The hits come from its skill
//! unit (skillunit/ontimer.rs).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::SkillImpl;
use crate::status::consts::SC_FREEZE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillStormGust;

impl SkillImpl for SkillStormGust {
    fn skill_id(&self) -> u16 {
        WZ_STORMGUST
    }

    /// SkillStormGust::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        //Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        *flag |= 1;
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }

    /// SkillStormGust::calculateSkillRatio (RENEWAL branch).
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio -= 30; // Offset only once
        *base_skillratio += 50 * skill_lv as i32;
    }

    /// SkillStormGust::applyAdditionalEffects: Storm Gust counter was dropped in renewal (RENEWAL branch; the
    /// pre-renewal sg_counter code is dropped).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let lv = skill_lv as i32;
        sc::sc_start(c, Some(src), target, SC_FREEZE, 65 - (5 * lv), lv, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }
}
