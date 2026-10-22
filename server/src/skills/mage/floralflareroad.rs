//! skills/mage/floralflareroad.cpp: class SkillFloralFlareRoad : public SkillImpl (AG_FLORAL_FLARE_ROAD), rAthena
//! e985006.
//!
//! The cast places the skill's unit group (skill_unitsetting; skillunit agent), whose hits go through skill_attack,
//! which asks the class for the skill ratio.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFloralFlareRoad;

impl SkillImpl for SkillFloralFlareRoad {
    fn skill_id(&self) -> u16 {
        AG_FLORAL_FLARE_ROAD
    }

    /// SkillFloralFlareRoad::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 50 + 740 * skill_lv as i32 + 5 * sstatus.spl as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillFloralFlareRoad::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
