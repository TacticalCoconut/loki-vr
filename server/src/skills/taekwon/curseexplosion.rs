//! skills/taekwon/curseexplosion.cpp: class SkillCurseExplosion : public SkillImplRecursiveDamageSplash (SP_CURSEEXPLOSION).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_SOULCURSE;
use crate::unit::Unit;
use crate::world::Ctx;

pub struct SkillCurseExplosion;

impl SkillImpl for SkillCurseExplosion {
    fn skill_id(&self) -> u16 {
        SP_CURSEEXPLOSION
    }

    crate::splash_skill_defaults!();

    /// SkillCurseExplosion::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let lv = skill_lv as i32;
        if sce(c, target, SC_SOULCURSE).is_some() {
            *skillratio += -100 + 1200 + 300 * lv;
        } else {
            *skillratio += -100 + 400 + 100 * lv;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillCurseExplosion {}
