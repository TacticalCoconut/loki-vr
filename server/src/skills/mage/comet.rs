//! skills/mage/comet.cpp: class SkillComet : public SkillImpl (WL_COMET), rAthena e985006. The hits come from the
//! skill's unit group (skillunit agent: skill_unitsetting / skill_unit_onplace_timer).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::status::consts::SC_MAGIC_POISON;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillComet;

impl SkillImpl for SkillComet {
    fn skill_id(&self) -> u16 {
        WL_COMET
    }

    /// SkillComet::applyAdditionalEffects: Magic Poison for 20 seconds.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_MAGIC_POISON, 100, skill_lv as i32, 20000);
    }

    /// SkillComet::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 2500 + 700 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillComet::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
