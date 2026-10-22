//! skills/ninja/shadowflash.cpp: class SkillShadowFlash : public SkillImplRecursiveDamageSplash (SS_KAGEGISSEN),
//! rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::SKILL_ALTDMG_FLAG;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::Ctx;

pub struct SkillShadowFlash;

impl SkillImpl for SkillShadowFlash {
    fn skill_id(&self) -> u16 {
        SS_KAGEGISSEN
    }

    crate::splash_skill_defaults!();

    /// SkillShadowFlash::calculateSkillRatio: 1500 + 950% per level, + 150% x level per Shadow Dance level, + 5 x POW,
    /// RE_LVL_DMOD(100); a mirage's hit (SKILL_ALTDMG_FLAG) deals 30%.
    fn calculate_skill_ratio(&self, c: &Ctx, wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 1500 + 950 * skill_lv as i32;
        *skillratio += pc_checkskill(c, src, SS_KAGENOMAI) * 150 * skill_lv as i32;
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        if wd.miscflag & SKILL_ALTDMG_FLAG != 0 {
            *skillratio = *skillratio * 3 / 10;
        }
    }
}

impl RecursiveDamageSplash for SkillShadowFlash {}
