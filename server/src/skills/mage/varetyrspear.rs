//! skills/mage/varetyrspear.cpp: class SkillVaretyrSpear : public SkillImplRecursiveDamageSplash (SO_VARETYR_SPEAR).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_BLAST_OPTION, SC_STUN};
use crate::status::{sc, status_get_status_data};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillVaretyrSpear;

impl SkillImpl for SkillVaretyrSpear {
    fn skill_id(&self) -> u16 {
        SO_VARETYR_SPEAR
    }

    crate::splash_skill_defaults!();

    /// SkillVaretyrSpear::applyAdditionalEffects: SC_STUN at 5 * lv percent for skill_get_time.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let lv = skill_lv as i32;
        sc::sc_start(c, Some(src), target, SC_STUN, 5 * lv, lv, skill_get_time(self.skill_id(), skill_lv) as i64);
    }

    /// SkillVaretyrSpear::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let int_ = status_get_status_data(c, src).int_ as i32;
        let lv = skill_lv as i32;

        *skillratio += -100 + (2 * int_ + 150 * (pc_checkskill(c, src, SO_STRIKING) + pc_checkskill(c, src, SA_LIGHTNINGLOADER)) + int_ * lv / 2) / 3;
        re_lvl_dmod(c, src, skillratio, 100);
        if sce(c, src, SC_BLAST_OPTION).is_some() {
            // sd ? sd->status.job_level * 5 : 0
            *skillratio += if src.kind == BL_PC { crate::pc::character_of(c, src.id).map_or(0, |ch| ch.job_level as i32 * 5) } else { 0 };
        }
    }
}

impl RecursiveDamageSplash for SkillVaretyrSpear {}
