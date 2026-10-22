//! skills/ninja/redflamecannon.cpp: class SkillRedFlameCannon : public SkillImplRecursiveDamageSplash (SS_SEKIENHOU),
//! rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_FIRE_CHARM_POWER;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRedFlameCannon;

impl SkillImpl for SkillRedFlameCannon {
    fn skill_id(&self) -> u16 {
        SS_SEKIENHOU
    }

    crate::splash_skill_defaults!();

    /// SkillRedFlameCannon::calculateSkillRatio: 600 + 1100% per level, + 70% x level per Darkening Cannon level, + 5
    /// x SPL, + 8500% under SC_FIRE_CHARM_POWER, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 600 + 1100 * skill_lv as i32;
        *skillratio += 70 * pc_checkskill(c, src, SS_ANTENPOU) * skill_lv as i32;
        *skillratio += 5 * sstatus.spl as i32;
        if sce(c, src, SC_FIRE_CHARM_POWER).is_some() {
            *skillratio += 8500;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillRedFlameCannon {
    /// SkillRedFlameCannon::splashSearch: the mirages cast Darkening Cannon, the animation, then the parent's.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        super::cannon_splash_search(self, c, src, target, skill_lv, tick, flag);
    }
}
