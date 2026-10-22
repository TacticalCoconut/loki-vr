//! skills/thief/omegaabyssstrike.cpp: class SkillOmegaAbyssStrike : public SkillImpl (ABC_ABYSS_STRIKE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface::skill_unitsetting;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::status::defs::{RC_ANGEL, RC_DEMON};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillOmegaAbyssStrike;

impl SkillImpl for SkillOmegaAbyssStrike {
    fn skill_id(&self) -> u16 {
        ABC_ABYSS_STRIKE
    }

    /// SkillOmegaAbyssStrike::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; //Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }

    /// SkillOmegaAbyssStrike::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let tstatus = crate::status::status_get_status_data(c, target);
        let lv = skill_lv as i32;
        *skillratio += -100 + 2650 * lv;
        *skillratio += 10 * sstatus.spl as i32;
        if tstatus.race as i32 == RC_DEMON || tstatus.race as i32 == RC_ANGEL {
            *skillratio += 200 * lv;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
