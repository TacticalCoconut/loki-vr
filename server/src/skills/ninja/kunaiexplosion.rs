//! skills/ninja/kunaiexplosion.cpp: class SkillKunaiExplosion : public SkillImplRecursiveDamageSplash (KO_BAKURETSU),
//! rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_KAGEMUSYA;
use crate::unit::Unit;
use crate::world::Ctx;

pub struct SkillKunaiExplosion;

impl SkillImpl for SkillKunaiExplosion {
    fn skill_id(&self) -> u16 {
        KO_BAKURETSU
    }

    crate::splash_skill_defaults!();

    /// SkillKunaiExplosion::calculateSkillRatio: Throwing Mastery level (1 for a non-player) x (50 + DEX / 4) x level
    /// x 4 / 10, RE_LVL_DMOD(120), + 10 x job level (1 for a non-player), then Shadow Warrior's bonus.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let is_pc = src.kind == BL_PC;
        let tobidougu = if is_pc { pc_checkskill(c, src, NJ_TOBIDOUGU) } else { 1 };
        *skillratio += -100 + tobidougu * (50 + sstatus.dex as i32 / 4) * skill_lv as i32 * 4 / 10;
        re_lvl_dmod(c, src, skillratio, 120);
        let job_level = if is_pc { crate::pc::character_of(c, src.id).map(|ch| ch.job_level as i32).unwrap_or(0) } else { 1 };
        *skillratio += 10 * job_level;
        if let Some(e) = sce(c, src, SC_KAGEMUSYA) {
            *skillratio += *skillratio * e.val2 / 100;
        }
    }
}

impl RecursiveDamageSplash for SkillKunaiExplosion {}
