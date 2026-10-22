//! skills/ninja/swirlingpetal.cpp: class SkillSwirlingPetal : public SkillImplRecursiveDamageSplash (KO_HUUMARANKA),
//! rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_KAGEMUSYA;
use crate::unit::Unit;
use crate::world::Ctx;

pub struct SkillSwirlingPetal;

impl SkillImpl for SkillSwirlingPetal {
    fn skill_id(&self) -> u16 {
        KO_HUUMARANKA
    }

    crate::splash_skill_defaults!();

    /// SkillSwirlingPetal::calculateSkillRatio: 150% per level + STR + 100% per Throw Huuma Shuriken level,
    /// RE_LVL_DMOD(100), then Shadow Warrior's bonus.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let huuma = if src.kind == BL_PC { pc_checkskill(c, src, NJ_HUUMA) * 100 } else { 0 };
        *skillratio += -100 + 150 * skill_lv as i32 + sstatus.str as i32 + huuma;
        re_lvl_dmod(c, src, skillratio, 100);
        if let Some(e) = sce(c, src, SC_KAGEMUSYA) {
            *skillratio += *skillratio * e.val2 / 100;
        }
    }
}

impl RecursiveDamageSplash for SkillSwirlingPetal {}
