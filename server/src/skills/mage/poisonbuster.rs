//! skills/mage/poisonbuster.cpp: class SkillPoisonBuster : public SkillImplRecursiveDamageSplash (SO_POISON_BUSTER).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_CLOUD_POISON, SC_CURSED_SOIL_OPTION};
use crate::status::status_get_status_data;
use crate::unit::Unit;
use crate::world::Ctx;

pub struct SkillPoisonBuster;

impl SkillImpl for SkillPoisonBuster {
    fn skill_id(&self) -> u16 {
        SO_POISON_BUSTER
    }

    crate::splash_skill_defaults!();

    /// SkillPoisonBuster::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = status_get_status_data(c, src);
        let lv = skill_lv as i32;

        *skillratio += -100 + 1000 + 300 * lv;
        *skillratio += sstatus.int_ as i32;
        if sce(c, target, SC_CLOUD_POISON).is_some() {
            *skillratio += 200 * lv;
        }
        re_lvl_dmod(c, src, skillratio, 100);
        if sce(c, src, SC_CURSED_SOIL_OPTION).is_some() {
            // sd ? sd->status.job_level * 5 : 0
            *skillratio += if src.kind == BL_PC { crate::pc::character_of(c, src.id).map_or(0, |ch| ch.job_level as i32 * 5) } else { 0 };
        }
    }
}

impl RecursiveDamageSplash for SkillPoisonBuster {}
