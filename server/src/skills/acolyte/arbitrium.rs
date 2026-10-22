//! skills/acolyte/arbitrium.cpp: class SkillArbitrium : public SkillImplRecursiveDamageSplash (CD_ARBITRIUM) and
//! class SkillArbitriumAttack : public SkillImplRecursiveDamageSplash (CD_ARBITRIUM_ATK).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::Ctx;

pub struct SkillArbitrium;

impl SkillImpl for SkillArbitrium {
    fn skill_id(&self) -> u16 {
        CD_ARBITRIUM
    }

    crate::splash_skill_defaults!();

    /// SkillArbitrium::calculateSkillRatio: 1200% per level + 10 x SPL (TODO (rAthena): spl ratio has changed ?)
    /// + 35% per Fidus Animus level per level (RE_LVL_DMOD 100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 1200 * skill_lv as i32;
        *skillratio += 10 * sstatus.spl as i32;
        *skillratio += 35 * pc_checkskill(c, src, CD_FIDUS_ANIMUS) * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillArbitrium {}

/// TODO (rAthena): CD_ARBITRIUM_ATK is no longer used ?
pub struct SkillArbitriumAttack;

impl SkillImpl for SkillArbitriumAttack {
    fn skill_id(&self) -> u16 {
        CD_ARBITRIUM_ATK
    }

    crate::splash_skill_defaults!();

    /// SkillArbitriumAttack::calculateSkillRatio: 1750% per level + 10 x SPL + 50% per Fidus Animus level per level
    /// (RE_LVL_DMOD 100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 1750 * skill_lv as i32 + 10 * sstatus.spl as i32;
        *skillratio += 50 * pc_checkskill(c, src, CD_FIDUS_ANIMUS) * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillArbitriumAttack {}
